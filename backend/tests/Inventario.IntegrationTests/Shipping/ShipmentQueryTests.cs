using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.IntegrationTests.Stock;
using Xunit;
using static Inventario.IntegrationTests.Shipping.ShippingApiSupport;

namespace Inventario.IntegrationTests.Shipping;

// HU-03 T012 (H-21; D-12; FR-006, FR-018, US1.2, US3.2, US4.1–US4.3): contexto de la salida sin ETag,
// con la revisión de cada línea en borradores, y lista paginada de las salidas de un pedido.
public sealed class ShipmentQueryTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task H21_the_detail_keeps_its_etag_when_the_article_is_renamed_and_the_context_follows()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var gestor = await support.SessionAsync("GestorMaestros");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "6");
        var (id, etag) = await CreateDraftAsync(operario, Body(seed, "2"));
        var (before, _) = await DetailAsync(gestor.Client, id);

        var context = await ContextAsync(gestor.Client, id);
        Assert.Equal(etag, context.GetProperty("shipmentVersion").GetString());
        Assert.Equal((await StockApiSupport.OrderAsync(gestor.Client, seed.Order)).ETag, context.GetProperty("orderVersion").GetString());
        Assert.True(context.TryGetProperty("asOf", out _));
        Assert.EndsWith("@h0.test", context.GetProperty("actorName").GetString());
        var order = context.GetProperty("order");
        Assert.Equal(seed.Order, order.GetProperty("id").GetGuid());
        Assert.Equal("Cliente Norte", order.GetProperty("customerReference").GetString());
        Assert.Equal("Open", order.GetProperty("state").GetString());
        Assert.Equal(seed.Sku, context.GetProperty("articles").EnumerateArray().Single().GetProperty("sku").GetString());
        Assert.Equal(seed.Location, context.GetProperty("locations").EnumerateArray().Single().GetProperty("id").GetGuid());
        Assert.True(context.GetProperty("lots").EnumerateArray().Single().GetProperty("isUntracked").GetBoolean());

        // Renombrar y desactivar el artículo no cambia el detalle ni su ETag; el contexto sí (US3.2).
        var articlePath = $"/api/v1/articles/{seed.Article:D}";
        using var current = await gestor.Client.GetAsync(articlePath);
        var family = (await current.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("familyId").GetGuid();
        using (var renamed = await gestor.WriteAsync(HttpMethod.Put, articlePath,
                   MasterDataArticleTests.Body(family, seed.Sku, name: "Nombre corregido", lot: false, active: false),
                   version: current.Headers.ETag!.ToString()))
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var (after, sameEtag) = await DetailAsync(gestor.Client, id);
        Assert.Equal(etag, sameEtag);
        Assert.Equal(before.GetRawText(), after.GetRawText());
        var refreshed = await ContextAsync(gestor.Client, id);
        var article = refreshed.GetProperty("articles").EnumerateArray().Single();
        Assert.Equal("Nombre corregido", article.GetProperty("name").GetString());
        Assert.False(article.GetProperty("isActive").GetBoolean());
        Assert.Equal(etag, refreshed.GetProperty("shipmentVersion").GetString());
    }

    // V-04 en la revisión: propio 4, ajeno 3, posible min(10, 4 + 3, 6) = 6 (acotado por el pendiente).
    [Fact]
    public async Task H21_the_review_of_a_draft_shows_own_other_and_possible_per_line_and_disappears_once_confirmed()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "6");
        var (orderB, lineB, _) = await StockApiSupport.CreateOrderAsync(operario, seed.Article, "5");
        await ReserveAsync(responsable, seed.Order, seed.OrderLine, seed.Location, seed.Lot, "4");
        await ReserveAsync(responsable, orderB, lineB, seed.Location, seed.Lot, "3");
        var (id, _) = await CreateDraftAsync(operario, Body(seed, "6"));
        var lineId = (await DetailAsync(operario.Client, id)).Body.GetProperty("lines")[0].GetProperty("id").GetGuid();

        var review = (await ContextAsync(operario.Client, id)).GetProperty("review").EnumerateArray().Single();
        Assert.Equal(lineId, review.GetProperty("lineId").GetGuid());
        Assert.Equal("6.000", review.GetProperty("pending").GetString());
        Assert.Equal("10.000", review.GetProperty("physical").GetString());
        Assert.Equal("7.000", review.GetProperty("reserved").GetString());
        Assert.Equal("4.000", review.GetProperty("ownReserved").GetString());
        Assert.Equal("3.000", review.GetProperty("otherReserved").GetString());
        Assert.Equal("6.000", review.GetProperty("possible").GetString());

        // Sin el límite del pendiente el posible es 7: pedido B (pendiente 5) desde el mismo bucket.
        var (draftB, _) = await CreateDraftAsync(operario, Body(orderB, DateTimeOffset.UtcNow.AddHours(-1), (lineB, seed.Location, seed.Lot, "5")));
        var reviewB = (await ContextAsync(operario.Client, draftB)).GetProperty("review").EnumerateArray().Single();
        Assert.Equal("3.000", reviewB.GetProperty("ownReserved").GetString());
        Assert.Equal("4.000", reviewB.GetProperty("otherReserved").GetString());
        Assert.Equal("5.000", reviewB.GetProperty("possible").GetString());

        using (var confirmed = await ConfirmAsync(operario, id, seed.Order))
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Empty((await ContextAsync(operario.Client, id)).GetProperty("review").EnumerateArray());
    }

    [Fact]
    public async Task H21_lists_the_shipments_of_an_order_newest_first_with_pages()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "10");
        var (first, _) = await CreateDraftAsync(operario, Body(seed, "2"));
        using (var confirmed = await ConfirmAsync(operario, first, seed.Order)) Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var (second, _) = await CreateDraftAsync(operario, Body(seed, "3"));
        using (var confirmed = await ConfirmAsync(operario, second, seed.Order)) Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var (draft, _) = await CreateDraftAsync(operario, Body(seed.Order, DateTimeOffset.UtcNow.AddHours(-1),
            (seed.OrderLine, seed.Location, seed.Lot, "1")));

        var reader = await support.SessionAsync("GestorMaestros");
        var page1 = await ListAsync(reader.Client, $"customerOrderId={seed.Order}&page=1&pageSize=2");
        Assert.Equal(3, page1.GetProperty("totalCount").GetInt32());
        var items = page1.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { draft, second }, items.Select(item => item.GetProperty("id").GetGuid()));
        Assert.Equal("Draft", items[0].GetProperty("state").GetString());
        Assert.Equal("Confirmed", items[1].GetProperty("state").GetString());
        Assert.Equal(1, items[1].GetProperty("lineCount").GetInt32());
        Assert.Equal("3.000", items[1].GetProperty("totalQuantity").GetString());
        Assert.EndsWith("@h0.test", items[1].GetProperty("actorName").GetString());
        var page2 = await ListAsync(reader.Client, $"customerOrderId={seed.Order}&page=2&pageSize=2");
        Assert.Equal(first, page2.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid());

        using (var missing = await reader.Client.GetAsync(Shipments))
            await StockApiSupport.ProblemAsync(missing, HttpStatusCode.UnprocessableEntity, "ValidationFailed");
        using (var invalid = await reader.Client.GetAsync($"{Shipments}?customerOrderId={seed.Order}&pageSize=0"))
            await StockApiSupport.ProblemAsync(invalid, HttpStatusCode.UnprocessableEntity, "ValidationFailed");
        using (var unknown = await reader.Client.GetAsync($"{Shipments}?customerOrderId={Guid.NewGuid()}"))
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task H21_context_and_list_need_inventory_read()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "6");
        var (id, _) = await CreateDraftAsync(operario, Body(seed, "1"));
        var admin = await support.SessionAsync("Administrador");
        using (var denied = await admin.Client.GetAsync($"{Shipments}/{id:D}/context"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await admin.Client.GetAsync($"{Shipments}?customerOrderId={seed.Order}"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var unknown = await operario.Client.GetAsync($"{Shipments}/{Guid.NewGuid():D}/context"))
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    private static async Task<JsonElement> ContextAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"{Shipments}/{id:D}/context");
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.OK == response.StatusCode, text);
        Assert.Null(response.Headers.ETag);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<JsonElement> ListAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync($"{Shipments}?{query}");
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.OK == response.StatusCode, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
