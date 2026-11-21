using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Receiving;
using Inventario.Api.Domain.Stock;
using Inventario.IntegrationTests.Purchasing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventario.IntegrationTests.Receiving;

public sealed class ReceivingMasterDataIntegrationTests(ReceivingFixture fixture) : IClassFixture<ReceivingFixture>
{
    [Fact]
    public async Task Removing_receipt_references_does_not_remove_purchase_protection()
    {
        var (order, line, article, location) = await fixture.SentOrderAsync();
        using var oper = await fixture.AsAsync(); using var master = await fixture.AsAsync("GestorMaestros");
        using var draft = await oper.WriteAsync(HttpMethod.Post, "/api/v1/receipts", ReceivingContractTests.Draft(order, line, location, Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        var articlePath = $"/api/v1/articles/{article}";
        using var before = await master.Client.GetAsync(articlePath);
        async Task RejectDelete()
        {
            using var delete = await master.WriteAsync(HttpMethod.Delete, articlePath, new { }, version: before.Headers.ETag!.ToString());
            Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        }
        await RejectDelete();
        using var revised = await oper.WriteAsync(HttpMethod.Put, draft.Headers.Location!.ToString(),
            new { purchaseOrderId = order, occurredAt = DateTimeOffset.UtcNow, lines = Array.Empty<object>() }, version: draft.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, revised.StatusCode);
        await RejectDelete();
        await using var db = fixture.Database.ApplicationDatabase();
        Assert.True(await db.Articles.AnyAsync(value => value.Id == article && value.IsDefinitionLocked));
        Assert.False(await db.Set<ReceiptLine>().AnyAsync(value => value.PurchaseOrderLineId == line));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Article_deactivation_and_receiving_serialize_in_both_orders(bool confirmFirst)
    {
        var (order, line, article, location) = await fixture.SentOrderAsync();
        var barrier = new MasterDataConcurrencyTests.SaveBarrier();
        using var support = new PurchasingTestSupport(fixture.Database, barrier);
        using var oper = await support.SessionAsync("Operario");
        using var manager = await support.SessionAsync("GestorMaestros");
        using var draft = await oper.WriteAsync(HttpMethod.Post, "/api/v1/receipts", ReceivingContractTests.Draft(order, line, location, Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        var locationPath = draft.Headers.Location!.ToString();
        var receiptId = Guid.Parse(locationPath.Split('/')[^1]);
        var articlePath = $"/api/v1/articles/{article:D}";
        using var previous = await manager.Client.GetAsync(articlePath);
        var data = await previous.Content.ReadFromJsonAsync<JsonElement>();
        var deactivate = MasterDataArticleTests.Body(data.GetProperty("familyId").GetGuid(), data.GetProperty("sku").GetString()!,
            name: data.GetProperty("name").GetString()!, lot: false, active: false);
        barrier.Predicate = confirmFirst
            ? entity => entity is Receipt receipt && receipt.Id == receiptId && receipt.State == ReceiptState.Confirmed
            : entity => entity is Article item && item.Id == article && !item.IsActive;
        Task<HttpResponseMessage> Confirm() => oper.WriteAsync(HttpMethod.Post, locationPath + "/confirm", new { }, version: draft.Headers.ETag!.ToString());
        Task<HttpResponseMessage> Deactivate() => manager.WriteAsync(HttpMethod.Put, articlePath, deactivate, version: previous.Headers.ETag!.ToString());
        var first = confirmFirst ? Confirm() : Deactivate();
        Task<HttpResponseMessage> second;
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = confirmFirst ? Deactivate() : Confirm();
            await MasterDataConcurrencyTests.WaitForApplicationLockAsync(fixture.Database);
        }
        finally { barrier.Release.TrySetResult(); }
        using var one = await first; using var two = await second;
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        Assert.Equal(confirmFirst ? HttpStatusCode.OK : HttpStatusCode.Conflict, two.StatusCode);
        await using var db = fixture.Database.ApplicationDatabase();
        var receiptAfter = await db.Set<Receipt>().SingleAsync(value => value.Id == receiptId);
        Assert.Equal(confirmFirst ? ReceiptState.Confirmed : ReceiptState.Draft, receiptAfter.State);
        Assert.Equal(confirmFirst ? 6m : 0m, await db.Set<StockEntry>().Where(value => value.ArticleId == article).SumAsync(value => value.PhysicalDelta));
        if (confirmFirst)
        {
            var movement = await db.Set<StockMovement>().SingleAsync(value => value.Id == receiptAfter.MovementId);
            Assert.Equal("ALB-6", movement.SupportReference); Assert.NotNull(movement.CounterpartyReference);
        }
    }
}
