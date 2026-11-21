using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventario.IntegrationTests.Purchasing;

public sealed class PurchasingMasterDataIntegrationTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task Removing_the_last_real_reference_keeps_definition_locked_but_allows_independent_deletion()
    {
        using var support = new PurchasingTestSupport(fixture);
        var (supplier, article) = await support.MastersAsync();
        var (_, other) = await support.MastersAsync();
        using var buyer = await support.SessionAsync(); using var manager = await support.SessionAsync("GestorMaestros");
        var keep = Guid.NewGuid();
        var body = new { supplierId = supplier, expectedDeliveryDate = "2026-12-01", lines = new[] {
            new { id = Guid.NewGuid(), articleId = article, quantity = "10", unitPrice = "0" },
            new { id = keep, articleId = other, quantity = "1", unitPrice = "0" } } };
        using var created = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();
        var path = $"/api/v1/articles/{article:D}";
        using var referenced = await manager.Client.GetAsync(path);
        using var blocked = await manager.WriteAsync(HttpMethod.Delete, path, new { }, version: referenced.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        using var edited = await buyer.WriteAsync(HttpMethod.Put, $"/api/v1/purchase-orders/{id:D}",
            PurchasingTestSupport.Draft(supplier, other, "1", keep), version: created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        await using var db = fixture.ApplicationDatabase();
        Assert.True((await db.Articles.SingleAsync(value => value.Id == article)).IsDefinitionLocked);
        Assert.False(await db.Set<PurchaseOrderLine>().AnyAsync(value => value.ArticleId == article));
        using var latest = await manager.Client.GetAsync(path);
        var articleDto = await latest.Content.ReadFromJsonAsync<JsonElement>();
        using var definition = await manager.WriteAsync(HttpMethod.Put, path,
            MasterDataArticleTests.Body(articleDto.GetProperty("familyId").GetGuid(), articleDto.GetProperty("sku").GetString()!, unit: "KG", fractions: true, lot: false),
            version: latest.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Conflict, definition.StatusCode);
        using var deleted = await manager.WriteAsync(HttpMethod.Delete, path, new { }, version: latest.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.True(await db.Set<PurchaseOrderAudit>().AnyAsync(value => value.PurchaseOrderId == id));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task First_purchase_reference_and_definition_or_delete_serialize_in_both_orders(bool referenceFirst, bool delete)
    {
        var barrier = new MasterDataConcurrencyTests.SaveBarrier();
        using var support = new PurchasingTestSupport(fixture, barrier);
        var (supplier, article) = await support.MastersAsync();
        using var buyer = await support.SessionAsync(); using var manager = await support.SessionAsync("GestorMaestros");
        var path = $"/api/v1/articles/{article:D}";
        using var before = await manager.Client.GetAsync(path);
        var dto = await before.Content.ReadFromJsonAsync<JsonElement>();
        barrier.Predicate = referenceFirst ? value => value is PurchaseOrderLine line && line.ArticleId == article
            : value => delete ? value is MasterDataAudit audit && audit.EntityId == article : value is Article item && item.Id == article;
        Task<HttpResponseMessage> Reference() => buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", PurchasingTestSupport.Draft(supplier, article, "1"));
        Task<HttpResponseMessage> Change() => manager.WriteAsync(delete ? HttpMethod.Delete : HttpMethod.Put, path,
            delete ? new { } : MasterDataArticleTests.Body(dto.GetProperty("familyId").GetGuid(), dto.GetProperty("sku").GetString()!, unit: "KG", fractions: true, lot: false),
            version: before.Headers.ETag!.ToString());
        Task<HttpResponseMessage> reference; Task<HttpResponseMessage> change;
        try
        {
            if (referenceFirst) { reference = Reference(); await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); change = Change(); }
            else { change = Change(); await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); reference = Reference(); }
            await MasterDataConcurrencyTests.WaitForApplicationLockAsync(fixture);
        }
        finally { barrier.Release.TrySetResult(); }
        using var referenced = await reference; using var changed = await change;
        Assert.Equal(!referenceFirst && delete ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.Created, referenced.StatusCode);
        Assert.Equal(referenceFirst ? HttpStatusCode.PreconditionFailed : delete ? HttpStatusCode.NoContent : HttpStatusCode.OK, changed.StatusCode);
        await using var db = fixture.ApplicationDatabase();
        if (referenceFirst || !delete)
        {
            var persisted = await db.Set<PurchaseOrderLine>().SingleAsync(value => value.ArticleId == article);
            Assert.Equal(referenceFirst ? "UD" : "KG", JsonDocument.Parse(persisted.ArticleSnapshot).RootElement.GetProperty("baseUnit").GetString());
            Assert.True((await db.Articles.SingleAsync(value => value.Id == article)).IsDefinitionLocked);
        }
        else Assert.False(await db.Set<PurchaseOrderLine>().AnyAsync(value => value.ArticleId == article));
    }

    [Fact]
    public async Task Supplier_changes_preserve_snapshots_and_reactivation_allows_new_purchases()
    {
        using var support = new PurchasingTestSupport(fixture);
        var (supplier, article) = await support.MastersAsync();
        using var buyer = await support.SessionAsync(); using var manager = await support.SessionAsync("GestorMaestros");
        using var created = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", PurchasingTestSupport.Draft(supplier, article));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var original = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource");
        var path = $"/api/v1/suppliers/{supplier:D}";
        using var before = await manager.Client.GetAsync(path);
        var master = await before.Content.ReadFromJsonAsync<JsonElement>();
        object Body(bool active) => new { code = master.GetProperty("code").GetString(), name = "Nombre corregido",
            email = "nuevo@proveedor.test", taxIdentifier = (string?)null, isActive = active };
        using var inactive = await manager.WriteAsync(HttpMethod.Put, path, Body(false), version: before.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, inactive.StatusCode);
        using var rejected = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", PurchasingTestSupport.Draft(supplier, article));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        var detail = await buyer.Client.GetFromJsonAsync<JsonElement>(created.Headers.Location!.ToString());
        Assert.Equal(original.GetProperty("supplierSnapshot").GetRawText(), detail.GetProperty("supplierSnapshot").GetRawText());
        using var reactivated = await manager.WriteAsync(HttpMethod.Put, path, Body(true), version: inactive.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
        using var next = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", PurchasingTestSupport.Draft(supplier, article));
        Assert.Equal(HttpStatusCode.Created, next.StatusCode);
        Assert.Equal("Nombre corregido", (await next.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("supplierSnapshot").GetProperty("name").GetString());
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Purchase_and_master_deactivation_respect_both_commit_orders(bool purchaseFirst, bool supplierChange)
    {
        var barrier = new MasterDataConcurrencyTests.SaveBarrier();
        using var support = new PurchasingTestSupport(fixture, barrier);
        var (supplier, article) = await support.MastersAsync();
        using var buyer = await support.SessionAsync(); using var manager = await support.SessionAsync("GestorMaestros");
        var target = supplierChange ? supplier : article;
        var path = $"/api/v1/{(supplierChange ? "suppliers" : "articles")}/{target:D}";
        using var before = await manager.Client.GetAsync(path);
        var master = await before.Content.ReadFromJsonAsync<JsonElement>();
        var body = JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(master.GetRawText())!;
        body.Remove("id"); body.Remove("isDefinitionLocked"); body["isActive"] = false;
        barrier.Predicate = purchaseFirst ? value => value is PurchaseOrderLine line && line.ArticleId == article
            : value => value is MasterDataAudit audit && audit.EntityId == target;
        Task<HttpResponseMessage> Purchase() => buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", PurchasingTestSupport.Draft(supplier, article));
        Task<HttpResponseMessage> Deactivate() => manager.WriteAsync(HttpMethod.Put, path, body, version: before.Headers.ETag!.ToString());
        Task<HttpResponseMessage> purchase; Task<HttpResponseMessage> deactivate;
        try
        {
            if (purchaseFirst) { purchase = Purchase(); await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); deactivate = Deactivate(); }
            else { deactivate = Deactivate(); await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); purchase = Purchase(); }
            await MasterDataConcurrencyTests.WaitForApplicationLockAsync(fixture);
        }
        finally { barrier.Release.TrySetResult(); }
        using var purchased = await purchase; using var changed = await deactivate;
        Assert.Equal(purchaseFirst ? HttpStatusCode.Created : HttpStatusCode.Conflict, purchased.StatusCode);
        if (purchaseFirst && !supplierChange)
        {
            Assert.Equal(HttpStatusCode.PreconditionFailed, changed.StatusCode);
            using var latest = await manager.Client.GetAsync(path);
            using var retried = await manager.WriteAsync(HttpMethod.Put, path, body, version: latest.Headers.ETag!.ToString());
            Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        }
        else Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        await using var db = fixture.ApplicationDatabase();
        Assert.Equal(purchaseFirst ? 1 : 0, await db.Set<PurchaseOrderLine>().CountAsync(value => value.ArticleId == article));
    }
}
