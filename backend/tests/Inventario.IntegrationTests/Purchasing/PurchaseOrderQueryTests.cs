using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Infrastructure.Messaging;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Inventario.IntegrationTests.Purchasing;

public sealed class PurchaseOrderQueryTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task List_filters_pages_and_out_of_range_keep_the_real_total_and_read_permission()
    {
        using var support = new PurchasingTestSupport(fixture); var (supplier, article) = await support.MastersAsync();
        using var buyer = await support.SessionAsync(); using var reader = await support.SessionAsync("Operario");
        using var first = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", PurchasingTestSupport.Draft(supplier, article));
        using var second = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", PurchasingTestSupport.Draft(supplier, article));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode); Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var number = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("number").GetString();
        var page = await reader.Client.GetFromJsonAsync<JsonElement>($"/api/v1/purchase-orders?supplierId={supplier:D}&state=Draft&pageSize=1&page=2");
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32()); Assert.Equal(1, page.GetProperty("items").GetArrayLength());
        Assert.Equal(number, page.GetProperty("items")[0].GetProperty("number").GetString());
        var empty = await reader.Client.GetFromJsonAsync<JsonElement>($"/api/v1/purchase-orders?supplierId={supplier:D}&page=99&pageSize=1");
        Assert.Equal(2, empty.GetProperty("totalCount").GetInt32()); Assert.Empty(empty.GetProperty("items").EnumerateArray());
        var search = await reader.Client.GetFromJsonAsync<JsonElement>($"/api/v1/purchase-orders?q={number!.ToLowerInvariant()}");
        Assert.Equal(1, search.GetProperty("totalCount").GetInt32());
        var future = await reader.Client.GetFromJsonAsync<JsonElement>($"/api/v1/purchase-orders?supplierId={supplier:D}&from=2099-01-01T00:00:00Z");
        Assert.Equal(0, future.GetProperty("totalCount").GetInt32());
        foreach (var query in new[] { "page=0", "pageSize=101", "state=unknown", "from=2026-12-01T00:00:00Z&to=2026-11-01T00:00:00Z", "from=2026-11-20" })
        {
            using var invalid = await reader.Client.GetAsync("/api/v1/purchase-orders?" + query);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        }
    }

    [Fact]
    public async Task A_snapshot_does_not_mix_the_count_before_a_concurrent_insert_with_rows_after_it()
    {
        var barrier = new CountBarrier();
        using var support = new PurchasingTestSupport(fixture, barrier); var (supplier, article) = await support.MastersAsync();
        using var buyer = await support.SessionAsync();
        using var first = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", PurchasingTestSupport.Draft(supplier, article));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        barrier.Arm();
        var reading = buyer.Client.GetAsync($"/api/v1/purchase-orders?supplierId={supplier:D}");
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var inserted = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", PurchasingTestSupport.Draft(supplier, article));
            Assert.Equal(HttpStatusCode.Created, inserted.StatusCode);
        }
        finally { barrier.Release.TrySetResult(); }
        using var response = await reading; Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32()); Assert.Equal(1, page.GetProperty("items").GetArrayLength());
        var current = await buyer.Client.GetFromJsonAsync<JsonElement>($"/api/v1/purchase-orders?supplierId={supplier:D}");
        Assert.Equal(2, current.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Query_only_receipt_preparation_verifies_projection_not_a_real_receiving_workflow()
    {
        var transport = new PurchaseDeliveryWorkerTests.ScriptedTransport();
        using var support = new PurchasingTestSupport(fixture, configure: services => {
            services.RemoveAll<IPurchaseMailTransport>(); services.AddSingleton<IPurchaseMailTransport>(transport);
        });
        using var buyer = await support.SessionAsync(); var job = await PurchaseDeliveryWorkerTests.PrepareAsync(support, buyer);
        Assert.True(await support.Factory.Services.GetRequiredService<PurchaseDeliveryWorker>().RunOnceAsync(CancellationToken.None));
        using var before = await buyer.Client.GetAsync(job.Path); Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        await using var db = fixture.ApplicationDatabase();
        var order = await db.Set<PurchaseOrder>().Include(value => value.Lines).SingleAsync(value => value.Id == job.OrderId);
        var line = Assert.Single(order.Lines);
        // Technical projection fixture only. HU-01 must supply the actual receipt/stock/operation atomically.
        line.SetReceivedQuantity(6m); order.DeriveReceiptState(); await db.SaveChangesAsync();
        using var partial = await buyer.Client.GetAsync(job.Path);
        var six = await partial.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PartiallyReceived", six.GetProperty("state").GetString());
        Assert.Equal("6.000", six.GetProperty("lines")[0].GetProperty("receivedQuantity").GetString());
        Assert.Equal("4.000", six.GetProperty("lines")[0].GetProperty("pendingQuantity").GetString());
        Assert.NotEqual(before.Headers.ETag, partial.Headers.ETag);
        line.SetReceivedQuantity(10m); order.DeriveReceiptState(); await db.SaveChangesAsync();
        var complete = await buyer.Client.GetFromJsonAsync<JsonElement>(job.Path);
        Assert.Equal("Received", complete.GetProperty("state").GetString());
        Assert.Equal("0.000", complete.GetProperty("lines")[0].GetProperty("pendingQuantity").GetString());
        var rejected = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.PurchaseOrderLines SET ReceivedQuantity=11 WHERE Id={line.Id}"));
        Assert.Equal(547, rejected.Number);
    }

    private sealed class CountBarrier : DbCommandInterceptor
    {
        private int armed;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Arm() => Interlocked.Exchange(ref armed, 1);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("COUNT(*)", StringComparison.Ordinal) && command.CommandText.Contains("[PurchaseOrders]", StringComparison.Ordinal)
                && Interlocked.CompareExchange(ref armed, 0, 1) == 1)
            { Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
            return result;
        }
    }
}
