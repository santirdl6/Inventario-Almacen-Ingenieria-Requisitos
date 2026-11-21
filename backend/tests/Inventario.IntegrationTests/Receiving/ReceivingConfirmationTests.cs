using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Domain.Purchasing;
using Inventario.IntegrationTests.Purchasing;
using Inventario.IntegrationTests.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests.Receiving;

public sealed class ReceivingConfirmationTests(ReceivingFixture fixture) : IClassFixture<ReceivingFixture>
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Receives_only_confirmed_quantities_and_never_exceeds_the_purchase(bool simultaneous, bool exceeds)
    {
        var (order, line, article, location) = await fixture.SentOrderAsync();
        using var first = await fixture.AsAsync(); using var second = await fixture.AsAsync();
        var orderPath = $"/api/v1/purchase-orders/{order}";
        using var initial = await first.Client.GetAsync(orderPath);
        var one = ReceivingContractTests.Draft(order, line, location, Guid.NewGuid(), Guid.NewGuid());
        var two = ReceivingContractTests.Draft(order, line, location, Guid.NewGuid(), Guid.NewGuid(), exceeds ? "6" : "4");
        using var savedOne = await first.WriteAsync(HttpMethod.Post, "/api/v1/receipts", one);
        using var savedTwo = await second.WriteAsync(HttpMethod.Post, "/api/v1/receipts", two);
        Assert.Equal(HttpStatusCode.Created, savedOne.StatusCode);
        Assert.Equal(HttpStatusCode.Created, savedTwo.StatusCode);
        await using (var db = fixture.Database.ApplicationDatabase())
            Assert.False(await db.Set<StockEntry>().AnyAsync(entry => entry.ArticleId == article));
        var key = Guid.NewGuid(); var confirmPath = savedOne.Headers.Location + "/confirm"; var tag = savedOne.Headers.ETag!.ToString();
        var a = first.WriteAsync(HttpMethod.Post, confirmPath, new { }, key, tag);
        if (!simultaneous) Assert.Equal(HttpStatusCode.OK, (await a).StatusCode);
        var b = second.WriteAsync(HttpMethod.Post, savedTwo.Headers.Location + "/confirm", new { }, version: savedTwo.Headers.ETag!.ToString());
        var result = await Task.WhenAll(a, b);
        Assert.Equal(exceeds ? 1 : 2, result.Count(value => value.StatusCode == HttpStatusCode.OK));
        if (exceeds) Assert.Single(result, value => value.StatusCode == HttpStatusCode.Conflict);
        using var current = await first.Client.GetAsync(orderPath);
        Assert.NotEqual(initial.Headers.ETag, current.Headers.ETag);
        var purchase = await current.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(exceeds ? "PartiallyReceived" : "Received", purchase.GetProperty("state").GetString());
        Assert.Equal(exceeds ? "4.000" : "0.000", purchase.GetProperty("lines")[0].GetProperty("pendingQuantity").GetString());
        if (result[0].StatusCode == HttpStatusCode.OK)
        {
            using var replay = await first.WriteAsync(HttpMethod.Post, confirmPath, new { }, key, tag);
            Assert.Equal(await result[0].Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        }
        await using var after = fixture.Database.ApplicationDatabase();
        var bucket = await after.Set<StockBalance>().SingleAsync(value => value.ArticleId == article);
        Assert.Equal(exceeds ? 6m : 10m, bucket.Physical);
        Assert.Equal(0m, bucket.Reserved);
        Assert.Equal(exceeds ? 1 : 2, await after.Set<StockEntry>().CountAsync(entry => entry.ArticleId == article));
    }

    [Theory]
    [InlineData("6", true)]
    [InlineData("4", false)]
    public async Task Concurrent_receipts_wait_on_the_same_purchase_before_revalidating(string secondAmount, bool exceeds)
    {
        var (order, line, article, location) = await fixture.SentOrderAsync();
        var barrier = new MasterDataConcurrencyTests.SaveBarrier();
        using var support = new PurchasingTestSupport(fixture.Database, barrier);
        using var first = await support.SessionAsync("Operario"); using var second = await support.SessionAsync("Operario");
        using var draft1 = await first.WriteAsync(HttpMethod.Post, "/api/v1/receipts",
            ReceivingContractTests.Draft(order, line, location, Guid.NewGuid(), Guid.NewGuid()));
        using var draft2 = await second.WriteAsync(HttpMethod.Post, "/api/v1/receipts",
            ReceivingContractTests.Draft(order, line, location, Guid.NewGuid(), Guid.NewGuid(), secondAmount));
        Assert.Equal(HttpStatusCode.Created, draft1.StatusCode); Assert.Equal(HttpStatusCode.Created, draft2.StatusCode);
        barrier.Predicate = entity => entity is PurchaseOrder purchase && purchase.Id == order && purchase.Lines.Any(value => value.ReceivedQuantity == 6m);
        var firstRequest = first.WriteAsync(HttpMethod.Post, draft1.Headers.Location + "/confirm", new { }, version: draft1.Headers.ETag!.ToString());
        Task<HttpResponseMessage> secondRequest;
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            secondRequest = second.WriteAsync(HttpMethod.Post, draft2.Headers.Location + "/confirm", new { }, version: draft2.Headers.ETag!.ToString());
            await MasterDataConcurrencyTests.WaitForApplicationLockAsync(fixture.Database);
        }
        finally { barrier.Release.TrySetResult(); }
        using var result1 = await firstRequest; using var result2 = await secondRequest;
        Assert.Equal(HttpStatusCode.OK, result1.StatusCode);
        Assert.Equal(exceeds ? HttpStatusCode.Conflict : HttpStatusCode.OK, result2.StatusCode);
        await using var db = fixture.Database.ApplicationDatabase();
        var balance = await db.Set<StockBalance>().SingleAsync(value => value.ArticleId == article && value.LocationId == location);
        Assert.Equal(exceeds ? 6m : 10m, balance.Physical);
        Assert.Equal(0m, balance.Reserved);
        Assert.Equal(exceeds ? 1 : 2, await db.Set<StockEntry>().CountAsync(value => value.ArticleId == article));
    }

    [Fact]
    public async Task Failure_after_writing_purchase_totals_rolls_back_and_same_intention_can_confirm()
    {
        var (order, line, article, location) = await fixture.SentOrderAsync();
        var failure = new FailAfterSaveInterceptor();
        using var support = new PurchasingTestSupport(fixture.Database, failure);
        using var oper = await support.SessionAsync("Operario");
        using var saved = await oper.WriteAsync(HttpMethod.Post, "/api/v1/receipts",
            ReceivingContractTests.Draft(order, line, location, Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        var path = saved.Headers.Location!.ToString(); var key = Guid.NewGuid(); var tag = saved.Headers.ETag!.ToString();
        failure.Arm();
        using var rejected = await oper.WriteAsync(HttpMethod.Post, path + "/confirm", new { }, key, tag);
        Assert.Equal(HttpStatusCode.InternalServerError, rejected.StatusCode);
        var purchase = await oper.Client.GetFromJsonAsync<JsonElement>($"/api/v1/purchase-orders/{order}");
        Assert.Equal("10.000", purchase.GetProperty("lines")[0].GetProperty("pendingQuantity").GetString());
        Assert.Equal("Draft", (await oper.Client.GetFromJsonAsync<JsonElement>(path)).GetProperty("state").GetString());
        await using var db = fixture.Database.ApplicationDatabase();
        Assert.False(await db.Set<StockEntry>().AnyAsync(value => value.ArticleId == article));
        Assert.False(await db.OperationResults.AnyAsync(value => value.OperationId == key));
        using var confirmed = await oper.WriteAsync(HttpMethod.Post, path + "/confirm", new { }, key, tag);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
    }

    [Fact]
    public async Task Lot_control_requires_a_lot_and_retains_the_effective_lot_on_each_allocation()
    {
        var (order, purchaseLine, article, location) = await fixture.SentOrderAsync(tracksLot: true);
        using var oper = await fixture.AsAsync();
        var line = Guid.NewGuid(); var allocation = Guid.NewGuid(); var happened = DateTimeOffset.UtcNow.AddMinutes(-1);
        object Input(string? lot) => new { purchaseOrderId = order, occurredAt = happened, supplierDocumentReference = "LOTE-1", reason = "Recepción",
            lines = new[] { new { id = line, purchaseOrderLineId = purchaseLine, reportedQuantity = "6", acceptedQuantity = "6",
                allocations = new[] { new { id = allocation, locationId = location, lotCode = lot, quantity = "6" } } } } };
        using var draft = await oper.WriteAsync(HttpMethod.Post, "/api/v1/receipts", Input(null));
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        var path = draft.Headers.Location!.ToString();
        using var rejected = await oper.WriteAsync(HttpMethod.Post, path + "/confirm", new { }, version: draft.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        using var edited = await oper.WriteAsync(HttpMethod.Put, path, Input("lote-01"), version: draft.Headers.ETag.ToString());
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        using var confirmed = await oper.WriteAsync(HttpMethod.Post, path + "/confirm", new { }, version: edited.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var result = await confirmed.Content.ReadFromJsonAsync<JsonElement>();
        var assigned = result.GetProperty("resource").GetProperty("lines")[0].GetProperty("allocations")[0];
        Assert.Equal("LOTE-01", assigned.GetProperty("lotCode").GetString());
        var lotId = assigned.GetProperty("lotId").GetGuid();
        await using var db = fixture.Database.ApplicationDatabase();
        Assert.True(await db.Set<StockEntry>().AnyAsync(value => value.ArticleId == article && value.LotId == lotId && value.PhysicalDelta == 6));
    }

    [Fact]
    public async Task Repeated_article_lines_keep_purchase_identity_while_sharing_the_same_bucket()
    {
        var masters = await fixture.Support.MastersAsync();
        using var buyer = await fixture.AsAsync("Comprador"); using var oper = await fixture.AsAsync();
        var firstLine = Guid.NewGuid(); var secondLine = Guid.NewGuid();
        using var order = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", new {
            supplierId = masters.SupplierId, expectedDeliveryDate = "2026-12-01", lines = new[] {
                new { id = firstLine, articleId = masters.ArticleId, quantity = "6", unitPrice = "1.00" },
                new { id = secondLine, articleId = masters.ArticleId, quantity = "4", unitPrice = "2.00" }
            }
        });
        Assert.Equal(HttpStatusCode.Created, order.StatusCode);
        var orderPath = order.Headers.Location!.ToString();
        var orderId = Guid.Parse(orderPath.Split('/')[^1]);
        var preview = await buyer.Client.GetFromJsonAsync<JsonElement>(orderPath + "/send-preview");
        using var sent = await buyer.WriteAsync(HttpMethod.Post, orderPath + "/send", new {
            kind = "initial", reviewToken = preview.GetProperty("reviewToken").GetString()
        }, version: order.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        Assert.True(await fixture.Support.Factory.Services.GetRequiredService<Inventario.Api.Infrastructure.Messaging.PurchaseDeliveryWorker>().RunOnceAsync(CancellationToken.None));
        var location = new Inventario.Api.Domain.Locations.Location(Guid.NewGuid(), "M-" + Guid.NewGuid().ToString("N")[..10],
            "A-" + Guid.NewGuid().ToString("N")[..10], "E01", "N01", Inventario.Api.Domain.Locations.LocationKind.Storage);
        await using (var db = fixture.Database.ApplicationDatabase()) { db.Add(location); await db.SaveChangesAsync(); }
        using var draft = await oper.WriteAsync(HttpMethod.Post, "/api/v1/receipts", new {
            purchaseOrderId = orderId, occurredAt = DateTimeOffset.UtcNow, supplierDocumentReference = "ALB-REPETIDO", reason = "Recepción",
            lines = new[] {
                new { id = Guid.NewGuid(), purchaseOrderLineId = firstLine, reportedQuantity = "6", acceptedQuantity = "6",
                    allocations = new[] { new { id = Guid.NewGuid(), locationId = location.Id, quantity = "6" } } },
                new { id = Guid.NewGuid(), purchaseOrderLineId = secondLine, reportedQuantity = "4", acceptedQuantity = "4",
                    allocations = new[] { new { id = Guid.NewGuid(), locationId = location.Id, quantity = "4" } } }
            }
        });
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        using var result = await oper.WriteAsync(HttpMethod.Post, draft.Headers.Location + "/confirm", new { }, version: draft.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var purchase = await oper.Client.GetFromJsonAsync<JsonElement>(orderPath);
        Assert.Equal("Received", purchase.GetProperty("state").GetString());
        var values = purchase.GetProperty("lines").EnumerateArray().ToDictionary(value => value.GetProperty("id").GetGuid());
        Assert.Equal("6.000", values[firstLine].GetProperty("receivedQuantity").GetString());
        Assert.Equal("4.000", values[secondLine].GetProperty("receivedQuantity").GetString());
        await using var after = fixture.Database.ApplicationDatabase();
        Assert.Single(await after.Set<StockEntry>().Where(value => value.ArticleId == masters.ArticleId).ToListAsync());
        Assert.Equal(10m, (await after.Set<StockBalance>().SingleAsync(value => value.ArticleId == masters.ArticleId)).Physical);
    }
}
