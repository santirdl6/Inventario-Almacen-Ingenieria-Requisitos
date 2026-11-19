using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Features.Stock.Core;
using Inventario.IntegrationTests.Fixtures;
using Inventario.IntegrationTests.Stock;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventario.IntegrationTests.Locations;

// Utilidades de HU-02 sobre las de la base común: sesión HTTP real de A (MasterDataApi) y
// existencias preparadas solo con el núcleo de stock (StockTestHarness), sin duplicarlas.
internal sealed class LocationsTestSupport(IdentityFixture fixture, ApiFactory factory) : IDisposable
{
    private readonly List<MasterDataApi> sessions = [];

    public IdentityFixture Fixture => fixture;
    public StockTestHarness Stock { get; } = new(fixture, factory.Services);

    /// <summary>Sesión con antiforgery. Operario tiene Inventory.Read y Locations.Manage;
    /// GestorMaestros solo lectura; Administrador ninguno de los dos.</summary>
    public async Task<MasterDataApi> SessionAsync(string role = "Operario")
    {
        var session = await MasterDataApi.LoginAsync(fixture, factory, role);
        sessions.Add(session);
        return session;
    }

    public static string Unique(int length = 8) => Guid.NewGuid().ToString("N")[..length].ToUpperInvariant();

    public static object Body(string code, string aisle, string rack = "E01", string level = "N01",
        string kind = "Storage") => new { code, aisle, rack, level, kind };

    public static Task<HttpResponseMessage> CreateAsync(MasterDataApi api, object body, Guid? key = null) =>
        api.WriteAsync(HttpMethod.Post, "/api/v1/locations", body, key);

    /// <summary>Crea una ubicación de un lugar único y devuelve su recurso y su ETag.</summary>
    public static async Task<Created> CreateLocationAsync(MasterDataApi api, string kind = "Storage", string? code = null)
    {
        var aisle = "P" + Unique(10);
        var response = await CreateAsync(api, Body(code ?? "L-" + Unique(12), aisle, kind: kind));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var resource = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource");
        return new(resource.GetProperty("id").GetGuid(), response.Headers.ETag!.ToString(), resource);
    }

    public static Task<HttpResponseMessage> UpdateAsync(MasterDataApi api, Guid id, JsonElement current, string etag,
        Guid? key = null, string? level = null, string? kind = null, bool? isActive = null, string? code = null) =>
        api.WriteAsync(HttpMethod.Put, $"/api/v1/locations/{id:D}", new
        {
            code = code ?? current.GetProperty("code").GetString(),
            aisle = current.GetProperty("aisle").GetString(),
            rack = current.GetProperty("rack").GetString(),
            level = level ?? current.GetProperty("level").GetString(),
            kind = kind ?? current.GetProperty("kind").GetString(),
            isActive = isActive ?? current.GetProperty("isActive").GetBoolean()
        }, key, etag);

    public static async Task<(JsonElement Body, string ETag)> GetAsync(MasterDataApi api, Guid id)
    {
        var response = await api.Client.GetAsync($"/api/v1/locations/{id:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(), response.Headers.ETag!.ToString());
    }

    public static async Task<string?> ProblemCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var code) ? code.GetString() : null;

    public async Task<int> AuditCountAsync(Guid locationId, MasterDataAction? action = null)
    {
        await using var database = fixture.ApplicationDatabase();
        return await database.Set<MasterDataAudit>().AsNoTracking().CountAsync(audit =>
            audit.EntityType == MasterDataEntity.Location && audit.EntityId == locationId
            && (action == null || audit.Action == action));
    }

    /// <summary>Artículo con existencias preparado con el núcleo (nunca con escrituras directas).</summary>
    public async Task<Guid> ArticleAsync(bool tracksLot = false)
    {
        var (article, _) = await Stock.SeedAsync(tracksLot: tracksLot);
        return article.Id;
    }

    public async Task ReceiveAsync(Guid article, Guid location, decimal quantity, string? lot = null)
    {
        var (_, principal) = await Stock.ActorAsync();
        await Stock.RunAsync(principal, StockCore.Prepare(StockOperation.Receive, StockTestHarness.Context("receipt"),
            [new(article, location, lot, quantity)]));
    }

    public async Task ReserveAsync(Guid article, Guid location, decimal quantity, string? lot = null)
    {
        var (_, principal) = await Stock.ActorAsync();
        await Stock.RunAsync(principal, StockCore.Prepare(StockOperation.Reserve, StockTestHarness.Context("reservation"),
            [new(article, location, lot, quantity)]));
    }

    public async Task ReleaseAsync(Guid article, Guid location, decimal quantity, string? lot = null)
    {
        var (_, principal) = await Stock.ActorAsync();
        await Stock.RunAsync(principal, StockCore.Prepare(StockOperation.Release, StockTestHarness.Context("release"),
            [new(article, location, lot, quantity)]));
    }

    public async Task ShipAsync(Guid article, Guid location, decimal quantity, decimal ownReserved = 0m, string? lot = null)
    {
        var (_, principal) = await Stock.ActorAsync();
        await Stock.RunAsync(principal, StockCore.PrepareShipment(StockTestHarness.Context("shipment"),
            [new(article, location, lot, quantity, ownReserved)]));
    }

    public void Dispose()
    {
        foreach (var session in sessions) session.Dispose();
    }

    internal sealed record Created(Guid Id, string ETag, JsonElement Resource);
}
