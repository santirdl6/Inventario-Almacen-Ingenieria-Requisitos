using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static Inventario.IntegrationTests.Locations.LocationsTestSupport;

namespace Inventario.IntegrationTests.Locations;

// US2 — Conocer todas las ubicaciones de un artículo (quickstart L-05 parte de artículo, L-06).
public sealed class ArticleLocationsTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private static Task<JsonElement> ReadAsync(Fixtures.MasterDataApi api, Guid article, string query = "") =>
        api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/articles/{article:D}/locations{query}");

    private static string[] Rows(JsonElement result) => result.GetProperty("items").EnumerateArray()
        .Select(item => $"{item.GetProperty("location").GetProperty("code").GetString()}|{item.GetProperty("lotCode").GetString()}|{item.GetProperty("physical").GetString()}")
        .ToArray();

    [Fact]
    public async Task L05_L06_lists_every_location_and_lot_with_the_total_of_the_whole_article()
    {
        using var factory = fixture.Factory();
        using var support = new LocationsTestSupport(fixture, factory);
        var api = await support.SessionAsync();
        var article = await support.ArticleAsync(tracksLot: true);
        var first = await CreateLocationAsync(api, code: "A-" + Unique(10));
        var second = await CreateLocationAsync(api, code: "B-" + Unique(10));

        await support.ReceiveAsync(article, first.Id, 2, "L-001");
        await support.ReceiveAsync(article, second.Id, 4, "L-001");
        await support.ReceiveAsync(article, second.Id, 3, "L-002");

        var result = await ReadAsync(api, article);
        Assert.Equal(3, result.GetProperty("totalCount").GetInt32());
        Assert.Equal(("9.000", "0.000"), (result.GetProperty("totals").GetProperty("physical").GetString(),
            result.GetProperty("totals").GetProperty("reserved").GetString()));
        var firstCode = first.Resource.GetProperty("code").GetString();
        var secondCode = second.Resource.GetProperty("code").GetString();
        Assert.Equal([$"{firstCode}|L-001|2.000", $"{secondCode}|L-001|4.000", $"{secondCode}|L-002|3.000"], Rows(result));
        Assert.Equal("UD", result.GetProperty("article").GetProperty("unit").GetString());
        await support.Stock.AssertJournalMatchesBalanceAsync(article, first.Id);
        await support.Stock.AssertJournalMatchesBalanceAsync(article, second.Id);

        // Los totales cubren todo el artículo, no la página.
        var paged = await ReadAsync(api, article, "?pageSize=1&page=2");
        Assert.Equal([$"{secondCode}|L-001|4.000"], Rows(paged));
        Assert.Equal("9.000", paged.GetProperty("totals").GetProperty("physical").GetString());

        // US2.5: un movimiento confirmado cambia el reparto sin pasos manuales.
        await support.ShipAsync(article, second.Id, 4, lot: "L-001");
        Assert.Equal([$"{firstCode}|L-001|2.000", $"{secondCode}|L-002|3.000"], Rows(await ReadAsync(api, article)));

        // US2.4: una ubicación inactiva sigue mostrando sus existencias con su estado.
        await support.DeactivateDirectlyAsync(first.Id);
        var withInactive = await ReadAsync(api, article);
        var inactiveRow = withInactive.GetProperty("items").EnumerateArray().First();
        Assert.False(inactiveRow.GetProperty("location").GetProperty("isActive").GetBoolean());
        Assert.Equal("5.000", withInactive.GetProperty("totals").GetProperty("physical").GetString());

        // US2.4: un artículo inactivo también sigue mostrando sus ubicaciones, con su estado
        // (preparación directa; la desactivación de artículos es de HU-08).
        await using (var database = fixture.ApplicationDatabase())
            await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlInterpolatedAsync(database.Database,
                $"UPDATE dbo.Articles SET IsActive = 0 WHERE Id = {article}");
        var inactiveArticle = await ReadAsync(api, article);
        Assert.False(inactiveArticle.GetProperty("article").GetProperty("isActive").GetBoolean());
        Assert.Equal(2, inactiveArticle.GetProperty("totalCount").GetInt32());
        Assert.Equal("5.000", inactiveArticle.GetProperty("totals").GetProperty("physical").GetString());
    }

    [Fact]
    public async Task An_article_without_stock_is_empty_not_an_error_and_unknown_articles_are_not_found()
    {
        using var factory = fixture.Factory();
        using var support = new LocationsTestSupport(fixture, factory);
        var api = await support.SessionAsync();
        var article = await support.ArticleAsync();

        var empty = await ReadAsync(api, article);
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        Assert.Equal("0.000", empty.GetProperty("totals").GetProperty("physical").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/api/v1/articles/{Guid.NewGuid():D}/locations")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await api.Client.GetAsync($"/api/v1/articles/{article:D}/locations?pageSize=0")).StatusCode);
    }

    [Fact]
    public async Task Accounts_without_inventory_read_cannot_query_article_locations()
    {
        using var factory = fixture.Factory();
        using var support = new LocationsTestSupport(fixture, factory);
        var admin = await support.SessionAsync("Administrador");
        var article = await support.ArticleAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Client.GetAsync($"/api/v1/articles/{article:D}/locations")).StatusCode);
    }
}
