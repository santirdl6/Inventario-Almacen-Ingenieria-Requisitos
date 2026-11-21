using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Inventario.IntegrationTests.Purchasing;

public sealed class PurchaseDeliveryRetryTests : IAsyncLifetime
{
    private readonly IdentityFixture fixture = new();
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task Known_failure_retry_uses_a_new_operation_but_preserves_the_exact_original_message()
    {
        var transport = new PurchaseDeliveryWorkerTests.ScriptedTransport { Result = new(false, true, false, "SmtpRejected", "Intento rechazado", DateTimeOffset.UtcNow) };
        using var support = new PurchasingTestSupport(fixture, configure: services => {
            services.RemoveAll<IPurchaseMailTransport>(); services.AddSingleton<IPurchaseMailTransport>(transport);
        });
        using var buyer = await support.SessionAsync(); var job = await PurchaseDeliveryWorkerTests.PrepareAsync(support, buyer);
        var worker = support.Factory.Services.GetRequiredService<PurchaseDeliveryWorker>(); await worker.RunOnceAsync(CancellationToken.None);
        using var failed = await buyer.Client.GetAsync(job.Path);
        var before = await failed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Failed", before.GetProperty("sendStatus").GetString());
        var supplier = before.GetProperty("supplierId").GetGuid();
        using var manager = await support.SessionAsync("GestorMaestros");
        using var master = await manager.Client.GetAsync($"/api/v1/suppliers/{supplier:D}");
        var values = await master.Content.ReadFromJsonAsync<JsonElement>();
        using var changed = await manager.WriteAsync(HttpMethod.Put, $"/api/v1/suppliers/{supplier:D}", new {
            code = values.GetProperty("code").GetString(), name = "Nombre posterior", email = "posterior@proveedor.test", isActive = false
        }, version: master.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var key = Guid.NewGuid(); var body = new { kind = "retry", deliveryId = job.DeliveryId };
        using var retried = await buyer.WriteAsync(HttpMethod.Post, job.Path + "/send", body, key, failed.Headers.ETag!.ToString());
        Assert.True(retried.StatusCode == HttpStatusCode.Accepted, await retried.Content.ReadAsStringAsync());
        var originalResponse = await retried.Content.ReadAsStringAsync();
        await using (var db = fixture.ApplicationDatabase())
        {
            var outbox = await db.Set<OutboxMessage>().SingleAsync(value => value.DeliveryId == job.DeliveryId);
            Assert.Equal(2, outbox.Cycle); Assert.Equal(1, outbox.Attempts); Assert.Equal(0, outbox.CycleAttempts);
            Assert.Equal(key, outbox.ActiveAuthorizationOperationId);
            Assert.Equal("compras@proveedor.test", (await db.Set<PurchaseOrderDelivery>().SingleAsync(value => value.Id == job.DeliveryId)).RecipientEmail);
        }
        using var replay = await buyer.WriteAsync(HttpMethod.Post, job.Path + "/send", body, key, failed.Headers.ETag!.ToString());
        Assert.Equal(originalResponse, await replay.Content.ReadAsStringAsync());
        transport.Result = new(true, false, false, null, null, DateTimeOffset.UtcNow);
        await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, transport.Control.Messages.Count);
        Assert.Equal(transport.Control.Messages.First(), transport.Control.Messages.Last());
        using var finalReplay = await buyer.WriteAsync(HttpMethod.Post, job.Path + "/send", body, key, failed.Headers.ETag!.ToString());
        Assert.Equal(originalResponse, await finalReplay.Content.ReadAsStringAsync());
        var current = await buyer.Client.GetFromJsonAsync<JsonElement>(job.Path);
        Assert.Equal("Accepted", current.GetProperty("sendStatus").GetString());
        Assert.False(await worker.RunOnceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_retries_only_open_one_cycle_and_pending_is_not_retryable()
    {
        var barrier = new MasterDataConcurrencyTests.SaveBarrier();
        var transport = new PurchaseDeliveryWorkerTests.ScriptedTransport { Result = new(false, true, false, "SmtpRejected", "Rechazado", DateTimeOffset.UtcNow) };
        using var support = new PurchasingTestSupport(fixture, barrier, services => { services.RemoveAll<IPurchaseMailTransport>(); services.AddSingleton<IPurchaseMailTransport>(transport); });
        using var buyer = await support.SessionAsync(); var job = await PurchaseDeliveryWorkerTests.PrepareAsync(support, buyer);
        await support.Factory.Services.GetRequiredService<PurchaseDeliveryWorker>().RunOnceAsync(CancellationToken.None);
        using var failed = await buyer.Client.GetAsync(job.Path);
        var body = new { kind = "retry", deliveryId = job.DeliveryId };
        barrier.Predicate = value => value is PurchaseOrder order && order.Id == job.OrderId;
        var first = buyer.WriteAsync(HttpMethod.Post, job.Path + "/send", body, version: failed.Headers.ETag!.ToString());
        Task<HttpResponseMessage> second;
        try { await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); second = buyer.WriteAsync(HttpMethod.Post, job.Path + "/send", body, version: failed.Headers.ETag!.ToString()); await MasterDataConcurrencyTests.WaitForApplicationLockAsync(fixture); }
        finally { barrier.Release.TrySetResult(); }
        using var accepted = await first; using var rejected = await second;
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode); Assert.Equal(HttpStatusCode.PreconditionFailed, rejected.StatusCode);
        using var pending = await buyer.WriteAsync(HttpMethod.Post, job.Path + "/send", body, version: accepted.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Conflict, pending.StatusCode);
        await using var db = fixture.ApplicationDatabase();
        Assert.Equal(2, (await db.Set<OutboxMessage>().SingleAsync(value => value.DeliveryId == job.DeliveryId)).Cycle);
        Assert.Equal(1, await db.Set<PurchaseOrderDelivery>().CountAsync(value => value.PurchaseOrderId == job.OrderId));
    }
}
