using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventario.IntegrationTests.Purchasing;

public sealed class PurchaseOrderSendConcurrencyTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_second_initial_or_edit_cannot_change_a_document_being_sent(bool editing)
    {
        var barrier = new MasterDataConcurrencyTests.SaveBarrier();
        using var support = new PurchasingTestSupport(fixture, barrier); using var buyer = await support.SessionAsync();
        var (supplier, article) = await support.MastersAsync(); var draft = PurchasingTestSupport.Draft(supplier, article);
        using var created = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", draft);
        var path = created.Headers.Location!.ToString(); var id = Guid.Parse(path.Split('/')[^1]);
        var preview = await buyer.Client.GetFromJsonAsync<JsonElement>(path + "/send-preview");
        var body = new { kind = "initial", reviewToken = preview.GetProperty("reviewToken").GetString() };
        barrier.Predicate = value => value is PurchaseOrderDelivery delivery && delivery.PurchaseOrderId == id;
        var first = buyer.WriteAsync(HttpMethod.Post, path + "/send", body, version: created.Headers.ETag!.ToString());
        Task<HttpResponseMessage> second;
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = editing ? buyer.WriteAsync(HttpMethod.Put, path, draft, version: created.Headers.ETag!.ToString())
                : buyer.WriteAsync(HttpMethod.Post, path + "/send", body, version: created.Headers.ETag!.ToString());
            await MasterDataConcurrencyTests.WaitForApplicationLockAsync(fixture);
        }
        finally { barrier.Release.TrySetResult(); }
        using var committed = await first; using var rejected = await second;
        Assert.Equal(HttpStatusCode.Accepted, committed.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, rejected.StatusCode);
        await using var db = fixture.ApplicationDatabase();
        Assert.Equal(1, await db.Set<PurchaseOrderDelivery>().CountAsync(value => value.PurchaseOrderId == id));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Initial_send_and_deactivation_serialize_in_both_orders(bool sendFirst, bool supplierChange)
    {
        var barrier = new MasterDataConcurrencyTests.SaveBarrier();
        using var support = new PurchasingTestSupport(fixture, barrier);
        var (supplier, article) = await support.MastersAsync();
        using var buyer = await support.SessionAsync(); using var manager = await support.SessionAsync("GestorMaestros");
        using var created = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", PurchasingTestSupport.Draft(supplier, article));
        var path = created.Headers.Location!.ToString();
        var preview = await buyer.Client.GetFromJsonAsync<JsonElement>(path + "/send-preview");
        Assert.True(preview.TryGetProperty("reviewToken", out var reviewed));
        var target = supplierChange ? supplier : article;
        var masterPath = $"/api/v1/{(supplierChange ? "suppliers" : "articles")}/{target:D}";
        using var before = await manager.Client.GetAsync(masterPath);
        var body = JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(await before.Content.ReadAsStringAsync())!;
        body.Remove("id"); body.Remove("isDefinitionLocked"); body["isActive"] = false;
        barrier.Predicate = sendFirst ? value => value is PurchaseOrderDelivery
            : value => value is MasterDataAudit audit && audit.EntityId == target;
        Task<HttpResponseMessage> Send() => buyer.WriteAsync(HttpMethod.Post, path + "/send", new { kind = "initial", reviewToken = reviewed.GetString() }, version: created.Headers.ETag!.ToString());
        Task<HttpResponseMessage> Deactivate() => manager.WriteAsync(HttpMethod.Put, masterPath, body, version: before.Headers.ETag!.ToString());
        Task<HttpResponseMessage> sending; Task<HttpResponseMessage> changing;
        try
        {
            if (sendFirst) { sending = Send(); await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); changing = Deactivate(); }
            else { changing = Deactivate(); await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); sending = Send(); }
            await MasterDataConcurrencyTests.WaitForApplicationLockAsync(fixture);
        }
        finally { barrier.Release.TrySetResult(); }
        using var sent = await sending; using var changed = await changing;
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(sendFirst ? HttpStatusCode.Accepted : HttpStatusCode.Conflict, sent.StatusCode);
        var id = Guid.Parse(path.Split('/')[^1]);
        await using var db = fixture.ApplicationDatabase();
        Assert.Equal(sendFirst ? 1 : 0, await db.Set<PurchaseOrderDelivery>().CountAsync(value => value.PurchaseOrderId == id));
        Assert.Equal(sendFirst ? PurchaseSendStatus.Pending : PurchaseSendStatus.None, (await db.Set<PurchaseOrder>().SingleAsync(value => value.Id == id)).SendStatus);
    }
}
