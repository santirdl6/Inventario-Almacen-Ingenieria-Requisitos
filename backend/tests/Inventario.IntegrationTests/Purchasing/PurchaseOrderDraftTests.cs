using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Domain.Stock;
using Inventario.IntegrationTests.Stock;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventario.IntegrationTests.Purchasing;

public sealed class PurchaseOrderDraftTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task Draft_is_numbered_replayable_editable_and_has_no_stock_effects()
    {
        using var support = new PurchasingTestSupport(fixture);
        var (supplier, article) = await support.MastersAsync();
        using var buyer = await support.SessionAsync();
        var settings = await buyer.Client.GetFromJsonAsync<JsonElement>("/api/v1/purchase-orders/settings");
        Assert.Equal("EUR", settings.GetProperty("currency").GetString());
        var line = Guid.NewGuid(); var key = Guid.NewGuid();
        var body = PurchasingTestSupport.Draft(supplier, article, lineId: line);
        using var created = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", body, key);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var original = await created.Content.ReadAsStringAsync();
        var order = JsonDocument.Parse(original).RootElement.GetProperty("resource");
        var id = order.GetProperty("id").GetGuid();
        var path = $"/api/v1/purchase-orders/{id:D}";
        Assert.Equal(path, created.Headers.Location!.ToString());
        Assert.Matches("^PO-[0-9]{8,}$", order.GetProperty("number").GetString()!);
        Assert.Equal("Draft", order.GetProperty("state").GetString());
        Assert.Equal("None", order.GetProperty("sendStatus").GetString());
        Assert.Equal("0.000", order.GetProperty("lines")[0].GetProperty("receivedQuantity").GetString());
        using var replay = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", body, key);
        Assert.Equal(original, await replay.Content.ReadAsStringAsync());
        Assert.Equal(created.Headers.ETag, replay.Headers.ETag);
        using var changed = await buyer.WriteAsync(HttpMethod.Put, path,
            PurchasingTestSupport.Draft(supplier, article, "6.000", line), version: created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.NotEqual(created.Headers.ETag, changed.Headers.ETag);
        using var stale = await buyer.WriteAsync(HttpMethod.Put, path, body, version: created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        using var missing = await buyer.WriteAsync(HttpMethod.Put, path, body);
        Assert.Equal((HttpStatusCode)428, missing.StatusCode);
        using var current = await buyer.Client.GetAsync(path);
        Assert.True(current.StatusCode == HttpStatusCode.OK, await current.Content.ReadAsStringAsync());
        Assert.Equal(changed.Headers.ETag, current.Headers.ETag);
        var context = await buyer.Client.GetFromJsonAsync<JsonElement>(path + "/context");
        Assert.Equal(current.Headers.ETag!.ToString(), context.GetProperty("purchaseOrderVersion").GetString());
        await using var db = fixture.ApplicationDatabase();
        Assert.Equal(2, await db.Set<PurchaseOrderAudit>().CountAsync(value => value.PurchaseOrderId == id));
        Assert.Empty(await db.Set<StockEntry>().ToListAsync());
        Assert.Empty(await db.Set<StockBalance>().ToListAsync());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("1.2345")]
    [InlineData("1000000000000000")]
    [InlineData("1e2")]
    public async Task Invalid_quantities_are_rejected_without_partial_order(string quantity)
    {
        using var support = new PurchasingTestSupport(fixture);
        var (supplier, article) = await support.MastersAsync();
        using var buyer = await support.SessionAsync();
        using var response = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", PurchasingTestSupport.Draft(supplier, article, quantity));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("lines[0].quantity", out _));
        await using var db = fixture.ApplicationDatabase();
        Assert.False(await db.Set<PurchaseOrder>().AnyAsync(value => value.SupplierId == supplier));
    }

    [Fact]
    public async Task Repeated_articles_are_independent_and_normalized_replay_preserves_original_response()
    {
        using var support = new PurchasingTestSupport(fixture);
        var (supplier, article) = await support.MastersAsync();
        using var buyer = await support.SessionAsync();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() }; var key = Guid.NewGuid();
        object Body(string quantity) => new { supplierId = supplier, expectedDeliveryDate = "2025-01-01",
            lines = ids.Select((id, i) => new { id, articleId = article, quantity, unitPrice = i == 0 ? "0" : "4.50", expectedDeliveryDate = i == 0 ? null : "2026-12-02" }).ToArray() };
        using var created = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", Body("6"), key);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var replay = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", Body("6.000"), key);
        Assert.Equal(await created.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        var order = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource");
        Assert.Equal(2, order.GetProperty("lines").GetArrayLength());
        Assert.Equal("0.00", order.GetProperty("lines")[0].GetProperty("unitPrice").GetString());
        Assert.Equal("2025-01-01", order.GetProperty("lines")[0].GetProperty("expectedDeliveryDate").GetString());
        using var conflict = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", Body("7"), key);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var manager = await support.SessionAsync("GestorMaestros");
        using var denied = await manager.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", Body("6"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var reordered = JsonSerializer.SerializeToNode(Body("6"))!;
        reordered["lines"] = new JsonArray(reordered["lines"]!.AsArray().Reverse().Select(value => value!.DeepClone()).ToArray());
        using var edited = await buyer.WriteAsync(HttpMethod.Put, created.Headers.Location!.ToString(), reordered, version: created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var lines = (await edited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("lines");
        Assert.Equal(ids[1], lines[0].GetProperty("id").GetGuid());
        Assert.Equal("4.50", lines[0].GetProperty("unitPrice").GetString());
        Assert.Equal(ids[0], lines[1].GetProperty("id").GetGuid());
        Assert.NotEqual(created.Headers.ETag, edited.Headers.ETag);
    }

    [Theory]
    [InlineData("price-negative")]
    [InlineData("price-scale")]
    [InlineData("invalid-date")]
    [InlineData("empty-lines")]
    [InlineData("unknown-field")]
    [InlineData("duplicate-line")]
    [InlineData("foreign-line")]
    [InlineData("changed-article")]
    [InlineData("missing-supplier")]
    public async Task Invalid_content_and_line_identity_are_rejected_atomically(string scenario)
    {
        using var support = new PurchasingTestSupport(fixture);
        var (supplier, article) = await support.MastersAsync();
        using var buyer = await support.SessionAsync();
        var body = JsonSerializer.SerializeToNode(PurchasingTestSupport.Draft(supplier, article))!;
        string? path = null; string? version = null;
        if (scenario is "foreign-line" or "changed-article")
        {
            using var created = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", body);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            if (scenario == "changed-article")
            {
                var (_, other) = await support.MastersAsync();
                body["lines"]![0]!["articleId"] = other;
                path = created.Headers.Location!.ToString(); version = created.Headers.ETag!.ToString();
            }
        }
        switch (scenario)
        {
            case "price-negative": body["lines"]![0]!["unitPrice"] = "-1"; break;
            case "price-scale": body["lines"]![0]!["unitPrice"] = "1.234"; break;
            case "invalid-date": body["expectedDeliveryDate"] = "2026-99-01"; break;
            case "empty-lines": body["lines"] = new JsonArray(); break;
            case "unknown-field": body["actorId"] = Guid.NewGuid(); break;
            case "duplicate-line": body["lines"]!.AsArray().Add(body["lines"]![0]!.DeepClone()); break;
            case "missing-supplier": body["supplierId"] = Guid.NewGuid(); break;
        }
        await using var db = fixture.ApplicationDatabase();
        var before = await db.Set<PurchaseOrder>().CountAsync(value => value.SupplierId == supplier);
        using var response = await buyer.WriteAsync(path is null ? HttpMethod.Post : HttpMethod.Put,
            path ?? "/api/v1/purchase-orders", body, version: version);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(before, await db.Set<PurchaseOrder>().CountAsync(value => value.SupplierId == supplier));
    }

    [Fact]
    public async Task Failure_after_writing_the_aggregate_rolls_back_lines_marker_audit_and_result()
    {
        var failure = new FailAfterSaveInterceptor();
        using var support = new PurchasingTestSupport(fixture, failure);
        var (supplier, article) = await support.MastersAsync();
        using var buyer = await support.SessionAsync();
        var key = Guid.NewGuid(); var body = PurchasingTestSupport.Draft(supplier, article);
        failure.Arm();
        using var failed = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", body, key);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        await using (var db = fixture.ApplicationDatabase())
        {
            Assert.False(await db.Set<PurchaseOrder>().AnyAsync(value => value.SupplierId == supplier));
            Assert.False(await db.Set<PurchaseOrderLine>().AnyAsync(value => value.ArticleId == article));
            Assert.False((await db.Articles.SingleAsync(value => value.Id == article)).IsDefinitionLocked);
            Assert.False(await db.OperationResults.AnyAsync(value => value.OperationId == key));
            Assert.False(await db.Set<PurchaseOrderAudit>().AnyAsync(value => value.OperationId == key));
        }
        using var retried = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", body, key);
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        await using var confirmed = fixture.ApplicationDatabase();
        Assert.Equal(1, await confirmed.Set<PurchaseOrder>().CountAsync(value => value.SupplierId == supplier));
        Assert.Equal(1, await confirmed.Set<PurchaseOrderAudit>().CountAsync(value => value.OperationId == key));
    }
}
