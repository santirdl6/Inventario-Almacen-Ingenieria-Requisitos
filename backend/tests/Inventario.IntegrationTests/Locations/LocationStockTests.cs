using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using static Inventario.IntegrationTests.Locations.LocationsTestSupport;

namespace Inventario.IntegrationTests.Locations;

// US3 — Revisar qué contiene una ubicación (quickstart L-05 parte de ubicación).
public sealed class LocationStockTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task L05_lists_article_lot_quantity_and_reservations_of_a_location()
    {
        using var factory = fixture.Factory();
        using var support = new LocationsTestSupport(fixture, factory);
        var api = await support.SessionAsync();
        var article = await support.ArticleAsync(tracksLot: true);
        var location = await CreateLocationAsync(api);
        await support.ReceiveAsync(article, location.Id, 4, "L-001");

        var stock = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/locations/{location.Id:D}/stock");
        var line = Assert.Single(stock.GetProperty("items").EnumerateArray());
        Assert.Equal(article, line.GetProperty("article").GetProperty("id").GetGuid());
        Assert.Equal(("L-001", "4.000", "0.000", "UD"), (line.GetProperty("lotCode").GetString(),
            line.GetProperty("physical").GetString(), line.GetProperty("reserved").GetString(), line.GetProperty("unit").GetString()));
        Assert.False(stock.GetProperty("hasReservations").GetBoolean());
        Assert.False(stock.GetProperty("inOpenCount").GetBoolean());

        // US3.3: las reservas se indican y aparecen en la fila.
        await support.ReserveAsync(article, location.Id, 1, "L-001");
        stock = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/locations/{location.Id:D}/stock");
        Assert.True(stock.GetProperty("hasReservations").GetBoolean());
        Assert.Equal("1.000", stock.GetProperty("items")[0].GetProperty("reserved").GetString());
        var (detail, _) = await GetAsync(api, location.Id);
        Assert.True(detail.GetProperty("hasReservations").GetBoolean());
        Assert.True(detail.GetProperty("hasMovements").GetBoolean());
        Assert.False(detail.GetProperty("isEmpty").GetBoolean());

        // US3.1: un artículo inactivo sigue en el contenido, con su estado (preparación directa;
        // la desactivación de artículos es de HU-08).
        await using (var database = fixture.ApplicationDatabase())
            await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlInterpolatedAsync(database.Database,
                $"UPDATE dbo.Articles SET IsActive = 0 WHERE Id = {article}");
        stock = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/locations/{location.Id:D}/stock");
        Assert.False(stock.GetProperty("items")[0].GetProperty("article").GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task An_empty_location_is_shown_as_empty_with_its_data_and_unknown_ones_are_not_found()
    {
        using var factory = fixture.Factory();
        using var support = new LocationsTestSupport(fixture, factory);
        var api = await support.SessionAsync();
        var location = await CreateLocationAsync(api, "Receiving");

        var stock = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/locations/{location.Id:D}/stock");
        Assert.Empty(stock.GetProperty("items").EnumerateArray());
        Assert.Equal(location.Resource.GetProperty("code").GetString(), stock.GetProperty("location").GetProperty("code").GetString());
        Assert.Equal("Receiving", stock.GetProperty("location").GetProperty("kind").GetString());
        Assert.True(stock.GetProperty("location").GetProperty("isActive").GetBoolean());

        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/api/v1/locations/{Guid.NewGuid():D}/stock")).StatusCode);
    }

    [Fact]
    public async Task Location_content_requires_a_session()
    {
        using var factory = fixture.Factory();
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/locations/{Guid.NewGuid():D}/stock")).StatusCode);
    }
}
