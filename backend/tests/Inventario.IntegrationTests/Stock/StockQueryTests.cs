using System.Net;
using System.Text.Json;
using Xunit;

namespace Inventario.IntegrationTests.Stock;

// HU-05 T007: GET /stock por HTTP con sesión real (contracts/stock-api.md § GET /stock).
// Habitual (S-01, S-05), error (S-03) y permisos. Existencias preparadas con StockCore.
public sealed class StockQueryTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task S01_rows_per_location_and_lot_with_totals_of_the_whole_filter()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var api = await support.SessionAsync("ResponsableAlmacen");
        var (article, sku) = await support.ArticleAsync(tracksLot: true);
        var prefix = "Q" + StockApiSupport.Unique(8);
        var n01 = await support.LocationAsync(prefix + "-N01");
        var n02 = await support.LocationAsync(prefix + "-N02");
        await support.ReceiveAsync(article, n01, 2m, "L-001");
        await support.ReceiveAsync(article, n02, 4m, "L-001");

        var (status, all) = await StockApiSupport.GetStockAsync(api.Client, $"?articleId={article}");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(sku, all.GetProperty("article").GetProperty("sku").GetString());
        Assert.Equal("UD", all.GetProperty("article").GetProperty("baseUnit").GetString());
        Assert.True(all.GetProperty("article").GetProperty("isActive").GetBoolean());
        Assert.True(all.GetProperty("article").GetProperty("tracksLot").GetBoolean());
        Assert.True(all.TryGetProperty("asOf", out _));
        Totals(all, "6.000", "0.000", "6.000");
        var items = all.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, all.GetProperty("totalCount").GetInt32());
        Assert.Equal([n01, n02], items.Select(item => item.GetProperty("location").GetProperty("id").GetGuid()));
        Assert.Equal(prefix + "-N01", items[0].GetProperty("location").GetProperty("code").GetString());
        Assert.Equal("L-001", items[0].GetProperty("lot").GetProperty("code").GetString());
        Assert.False(items[0].GetProperty("lot").GetProperty("isUntracked").GetBoolean());
        Assert.Equal("2.000", items[0].GetProperty("physical").GetString());
        Assert.Equal("2.000", items[0].GetProperty("available").GetString());

        // Filtro por ubicación: filas y totales solo de N02.
        var (_, byLocation) = await StockApiSupport.GetStockAsync(api.Client, $"?articleId={article}&locationId={n02}");
        Assert.Single(byLocation.GetProperty("items").EnumerateArray());
        Totals(byLocation, "4.000", "0.000", "4.000");

        // Filtro por lote: las dos filas del lote L-001.
        var lot = items[0].GetProperty("lot").GetProperty("id").GetGuid();
        var (_, byLot) = await StockApiSupport.GetStockAsync(api.Client, $"?articleId={article}&lotId={lot}");
        Assert.Equal(2, byLot.GetProperty("totalCount").GetInt32());

        // Paginación: los totales siguen siendo del filtro completo.
        var (_, first) = await StockApiSupport.GetStockAsync(api.Client, $"?articleId={article}&page=1&pageSize=1");
        Assert.Single(first.GetProperty("items").EnumerateArray());
        Assert.Equal(2, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, first.GetProperty("pageSize").GetInt32());
        Totals(first, "6.000", "0.000", "6.000");
        var (_, second) = await StockApiSupport.GetStockAsync(api.Client, $"?articleId={article}&page=2&pageSize=1");
        Assert.Equal(n02, second.GetProperty("items")[0].GetProperty("location").GetProperty("id").GetGuid());
        Assert.Equal(2, second.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task Available_is_physical_minus_reserved_per_row_and_in_the_totals()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var api = await support.SessionAsync("Operario");
        var (article, _) = await support.ArticleAsync();
        var location = await support.LocationAsync("R" + StockApiSupport.Unique(8));
        await support.ReceiveAsync(article, location, 12m);
        await support.ReserveAsync(article, location, 8m);

        var (status, body) = await StockApiSupport.GetStockAsync(api.Client, $"?articleId={article}");
        Assert.Equal(HttpStatusCode.OK, status);
        var row = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal("12.000", row.GetProperty("physical").GetString());
        Assert.Equal("8.000", row.GetProperty("reserved").GetString());
        Assert.Equal("4.000", row.GetProperty("available").GetString());
        Totals(body, "12.000", "8.000", "4.000");
        // Artículo sin seguimiento: bucket propio SIN-LOTE, marcado para que la UI diga «sin lote».
        Assert.True(row.GetProperty("lot").GetProperty("isUntracked").GetBoolean());
    }

    [Fact]
    public async Task S05_buckets_with_zero_physical_are_hidden_by_default_and_totals_do_not_change()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var api = await support.SessionAsync("Operario");
        var (article, _) = await support.ArticleAsync();
        var prefix = "Z" + StockApiSupport.Unique(8);
        var n01 = await support.LocationAsync(prefix + "-N01");
        var n02 = await support.LocationAsync(prefix + "-N02");
        await support.ReceiveAsync(article, n01, 2m);
        await support.ReceiveAsync(article, n02, 4m);
        await support.ShipAsync(article, n01, 2m);

        var (_, hidden) = await StockApiSupport.GetStockAsync(api.Client, $"?articleId={article}");
        var visible = Assert.Single(hidden.GetProperty("items").EnumerateArray());
        Assert.Equal(n02, visible.GetProperty("location").GetProperty("id").GetGuid());
        Assert.Equal(1, hidden.GetProperty("totalCount").GetInt32());
        Totals(hidden, "4.000", "0.000", "4.000");

        var (_, shown) = await StockApiSupport.GetStockAsync(api.Client, $"?articleId={article}&includeEmpty=true");
        var rows = shown.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(n01, rows[0].GetProperty("location").GetProperty("id").GetGuid());
        Assert.Equal("0.000", rows[0].GetProperty("physical").GetString());
        Assert.Equal("0.000", rows[0].GetProperty("reserved").GetString());
        Assert.Equal("0.000", rows[0].GetProperty("available").GetString());
        Totals(shown, "4.000", "0.000", "4.000");
    }

    [Fact]
    public async Task S03_requires_a_valid_article_and_rejects_out_of_range_pages()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var api = await support.SessionAsync("Operario");
        var (article, _) = await support.ArticleAsync();
        await support.ReceiveAsync(article, await support.LocationAsync("E" + StockApiSupport.Unique(8)), 1m);

        foreach (var query in new[] { "", "?articleId=", "?articleId=no-es-un-uuid" })
        {
            var (status, problem) = await StockApiSupport.GetStockAsync(api.Client, query);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
            Assert.Equal("ValidationFailed", problem.GetProperty("code").GetString());
            Assert.True(problem.GetProperty("errors").TryGetProperty("articleId", out _), query);
        }

        var (missing, _) = await StockApiSupport.GetStockAsync(api.Client, $"?articleId={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing);

        // Ubicación o lote ajenos o inexistentes: 200 sin filas y con totales a cero, sin error técnico.
        var foreign = await support.LocationAsync("F" + StockApiSupport.Unique(8));
        foreach (var filter in new[] { $"&locationId={foreign}", $"&lotId={Guid.NewGuid()}" })
        {
            var (status, empty) = await StockApiSupport.GetStockAsync(api.Client, $"?articleId={article}{filter}");
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Empty(empty.GetProperty("items").EnumerateArray());
            Assert.Equal(0, empty.GetProperty("totalCount").GetInt32());
            Totals(empty, "0.000", "0.000", "0.000");
        }

        foreach (var (query, field) in new[] { ("&page=0", "page"), ("&pageSize=0", "pageSize"), ("&pageSize=101", "pageSize") })
        {
            var (status, problem) = await StockApiSupport.GetStockAsync(api.Client, $"?articleId={article}{query}");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
            Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), query);
        }
    }

    [Fact]
    public async Task Inventory_read_is_required_and_checked_by_the_server()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var (article, _) = await support.ArticleAsync();
        var query = $"?articleId={article}";

        using (var anonymous = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
               { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false }))
            Assert.Equal(HttpStatusCode.Unauthorized, (await StockApiSupport.GetStockAsync(anonymous, query)).Status);

        Assert.Equal(HttpStatusCode.Forbidden, (await StockApiSupport.GetStockAsync((await support.SessionAsync("Administrador")).Client, query)).Status);
        foreach (var role in new[] { "GestorMaestros", "Operario", "ResponsableAlmacen" })
            Assert.Equal(HttpStatusCode.OK, (await StockApiSupport.GetStockAsync((await support.SessionAsync(role)).Client, query)).Status);
    }

    private static void Totals(JsonElement body, string physical, string reserved, string available)
    {
        var totals = body.GetProperty("totals");
        Assert.Equal(physical, totals.GetProperty("physical").GetString());
        Assert.Equal(reserved, totals.GetProperty("reserved").GetString());
        Assert.Equal(available, totals.GetProperty("available").GetString());
    }
}
