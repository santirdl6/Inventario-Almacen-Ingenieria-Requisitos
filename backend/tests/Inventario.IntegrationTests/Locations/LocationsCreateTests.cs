using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Inventario.IntegrationTests.Locations.LocationsTestSupport;

namespace Inventario.IntegrationTests.Locations;

// US1 — Registrar las ubicaciones físicas del almacén (quickstart L-01–L-03).
public sealed class LocationsCreateTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task L01_creates_active_empty_locations_that_can_be_found_again()
    {
        using var factory = fixture.Factory();
        using var support = new LocationsTestSupport(fixture, factory);
        var api = await support.SessionAsync();
        var aisle = "P" + Unique(6);

        var receiving = await CreateAsync(api, Body("REC-" + Unique(), "R" + Unique(6), kind: "Receiving"));
        var first = await CreateAsync(api, Body(" " + aisle.ToLowerInvariant() + "-e01-n01 ", " " + aisle.ToLowerInvariant() + " ", "e01", "n01"));
        var second = await CreateAsync(api, Body(aisle + "-E01-N02", aisle, "E01", "N02"));
        foreach (var response in new[] { receiving, first, second })
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource");
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/locations/{id:D}", first.Headers.Location!.OriginalString);
        Assert.Equal(aisle + "-E01-N01", created.GetProperty("code").GetString());
        Assert.Equal(aisle, created.GetProperty("aisle").GetString());
        Assert.Equal($"Pasillo {aisle} · Estantería E01 · Nivel N01", created.GetProperty("description").GetString());
        Assert.Equal(("Almacenamiento", "Activa"), (created.GetProperty("kindLabel").GetString(), created.GetProperty("statusLabel").GetString()));
        Assert.True(created.GetProperty("isEmpty").GetBoolean());
        Assert.False(created.GetProperty("hasMovements").GetBoolean());

        var (read, etag) = await GetAsync(api, id);
        Assert.Equal(first.Headers.ETag!.ToString(), etag);
        Assert.Equal(created.GetProperty("code").GetString(), read.GetProperty("code").GetString());

        Assert.Equal(1, await support.AuditCountAsync(id, MasterDataAction.Create));
        await using (var database = fixture.ApplicationDatabase())
        {
            Assert.False(await database.Set<StockBalance>().AnyAsync(balance => balance.LocationId == id));
            Assert.False(await database.Set<StockEntry>().AnyAsync(entry => entry.LocationId == id));
        }

        // US1.5: prefijo de pasillo o de código, filtros de tipo y estado; activas por defecto.
        var byAisle = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/locations?search={aisle.ToLowerInvariant()}");
        Assert.Equal(2, byAisle.GetProperty("totalCount").GetInt32());
        Assert.Equal(new string?[] { aisle + "-E01-N01", aisle + "-E01-N02" }, byAisle.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("code").GetString()).ToArray());
        var byCode = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/locations?search={aisle}-E01-N02");
        Assert.Equal(1, byCode.GetProperty("totalCount").GetInt32());
        var receivingOnly = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/locations?search={aisle}&kind=Receiving");
        Assert.Equal(0, receivingOnly.GetProperty("totalCount").GetInt32());
        var inactive = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/locations?search={aisle}&status=inactive");
        Assert.Empty(inactive.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task L02_rejects_repeated_codes_and_places_also_against_inactive_locations()
    {
        using var factory = fixture.Factory();
        using var support = new LocationsTestSupport(fixture, factory);
        var api = await support.SessionAsync();
        var aisle = "P" + Unique(6);
        var code = aisle + "-E01-N01";
        var existing = await CreateAsync(api, Body(code, aisle));
        Assert.Equal(HttpStatusCode.Created, existing.StatusCode);
        var inactive = await CreateLocationAsync(api);
        await support.DeactivateDirectlyAsync(inactive.Id); // preparación; la desactivación por API es US4

        var sameCode = await CreateAsync(api, Body(" " + code.ToLowerInvariant() + " ", "Q" + Unique(6)));
        Assert.Equal(HttpStatusCode.Conflict, sameCode.StatusCode);
        var problem = await sameCode.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DuplicateCode", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("code", out _));

        var inactiveCode = inactive.Resource.GetProperty("code").GetString()!;
        Assert.Equal("DuplicateCode", await ProblemCodeAsync(await CreateAsync(api, Body(inactiveCode, "Q" + Unique(6)))));

        // US1.3: el lugar repetido identifica la ubicación ya registrada, también si está inactiva.
        var samePlace = await CreateAsync(api, Body("OTRO-" + Unique(), aisle.ToLowerInvariant(), "e01", "n01"));
        Assert.Equal(HttpStatusCode.Conflict, samePlace.StatusCode);
        problem = await samePlace.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DuplicateLocationPlace", problem.GetProperty("code").GetString());
        Assert.Contains(code, problem.GetProperty("detail").GetString());
        foreach (var field in new[] { "aisle", "rack", "level" })
            Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
        var inactivePlace = await CreateAsync(api, Body("OTRO-" + Unique(), inactive.Resource.GetProperty("aisle").GetString()!));
        problem = await inactivePlace.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DuplicateLocationPlace", problem.GetProperty("code").GetString());
        Assert.Contains("(inactiva)", problem.GetProperty("detail").GetString());

        var search = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/locations?search=OTRO-&status=all");
        Assert.Equal(0, search.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task L02_two_simultaneous_creations_of_the_same_place_leave_one_location()
    {
        using var factory = fixture.Factory();
        using var support = new LocationsTestSupport(fixture, factory);
        var api = await support.SessionAsync();
        for (var round = 0; round < 5; round++)
        {
            var aisle = "C" + Unique(8);
            var responses = await Task.WhenAll(
                CreateAsync(api, Body("A-" + Unique(), aisle)), CreateAsync(api, Body("B-" + Unique(), aisle)));
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            var rejected = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            var body = await rejected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("DuplicateLocationPlace", body.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task L03_requires_every_physical_part_and_valid_lengths_and_pages()
    {
        using var factory = fixture.Factory();
        using var support = new LocationsTestSupport(fixture, factory);
        var api = await support.SessionAsync();
        var code = "BLANK-" + Unique();

        var blank = await CreateAsync(api, Body(code, "   ", kind: "Receiving"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blank.StatusCode);
        var problem = await blank.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ValidationFailed", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("aisle", out _));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await CreateAsync(api, Body(new string('X', 33), "P" + Unique(6)))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await CreateAsync(api, Body(code, new string('P', 17)))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await CreateAsync(api, Body(code, "P" + Unique(6), kind: "Pasillo"))).StatusCode);
        var unknownField = await api.WriteAsync(HttpMethod.Post, "/api/v1/locations",
            new { code, aisle = "P" + Unique(6), rack = "E01", level = "N01", kind = "Storage", zone = "Z1" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknownField.StatusCode);

        var page = await api.Client.GetAsync("/api/v1/locations?page=0");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, page.StatusCode);
        Assert.True((await page.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("page", out _));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await api.Client.GetAsync("/api/v1/locations?pageSize=101")).StatusCode);

        var none = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/locations?search={code}&status=all");
        Assert.Equal(0, none.GetProperty("totalCount").GetInt32());

        // Contrato: el detalle de una ubicación inexistente es 404 ProblemDetails.
        var missing = await api.Client.GetAsync($"/api/v1/locations/{Guid.NewGuid():D}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType!.MediaType);
        Assert.Equal("NotFound", await ProblemCodeAsync(missing));
    }

    [Fact]
    public async Task Read_only_accounts_can_search_but_not_create()
    {
        using var factory = fixture.Factory();
        using var support = new LocationsTestSupport(fixture, factory);
        var reader = await support.SessionAsync("GestorMaestros");
        var code = "RO-" + Unique();

        var rejected = await CreateAsync(reader, Body(code, "P" + Unique(6)));
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.Client.GetAsync("/api/v1/locations")).StatusCode);
        var none = await reader.Client.GetFromJsonAsync<JsonElement>($"/api/v1/locations?search={code}&status=all");
        Assert.Equal(0, none.GetProperty("totalCount").GetInt32());
    }
}
