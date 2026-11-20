using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Features.Stock.Core;
using Inventario.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Inventario.IntegrationTests.Stock;

/// <summary>
/// Soporte HTTP de las pruebas de HU-05 (T007). Abre sesión real con antiforgery por rol
/// (Operario, ResponsableAlmacen, GestorMaestros, Administrador) y prepara existencias solo con
/// StockCore, mediante <see cref="StockTestHarness"/> y sin modificarlo; nunca escribe saldos.
/// </summary>
/// <summary>Escritores de otros agregados en las carreras (mismo orden que en las pruebas).</summary>
public enum StockWriter { ArticleDeactivate, ArticleDefinition, ArticleDelete, LocationDeactivate }

internal sealed class StockApiSupport(IdentityFixture fixture, ApiFactory factory) : IDisposable
{
    private readonly List<MasterDataApi> sessions = [];

    public StockTestHarness Stock { get; } = new(fixture, factory.Services);

    public async Task<MasterDataApi> SessionAsync(string role = "Operario")
    {
        var session = await MasterDataApi.LoginAsync(fixture, factory, role);
        sessions.Add(session);
        return session;
    }

    /// <summary>Artículo de referencia técnica (familia del fixture), activo, sin existencias.</summary>
    public async Task<(Guid Id, string Sku)> ArticleAsync(bool tracksLot = false)
    {
        var sku = "S05-" + Unique(10);
        var article = await fixture.NewArticleAsync(sku, "Artículo de consulta", "UD", false, tracksLot);
        await using var database = fixture.ApplicationDatabase();
        database.Articles.Add(article);
        await database.SaveChangesAsync();
        return (article.Id, sku);
    }

    /// <summary>Ubicación de almacenamiento con un código que fija el orden de la consulta.</summary>
    public async Task<Guid> LocationAsync(string code)
    {
        var location = new Location(code, "P" + Unique(6), "E01", "N01", LocationKind.Storage);
        await using var database = fixture.ApplicationDatabase();
        database.Add(location);
        await database.SaveChangesAsync();
        return location.Id;
    }

    public Task ReceiveAsync(Guid article, Guid location, decimal quantity, string? lot = null) =>
        RunAsync(StockCore.Prepare(StockOperation.Receive, StockTestHarness.Context("receipt"), [new(article, location, lot, quantity)]));

    public Task ReserveAsync(Guid article, Guid location, decimal quantity, string? lot = null) =>
        RunAsync(StockCore.Prepare(StockOperation.Reserve, StockTestHarness.Context("reservation"), [new(article, location, lot, quantity)]));

    public Task ShipAsync(Guid article, Guid location, decimal quantity, string? lot = null) =>
        RunAsync(StockCore.PrepareShipment(StockTestHarness.Context("shipment"), [new(article, location, lot, quantity, 0m)]));

    public static async Task<(HttpStatusCode Status, JsonElement Body)> GetStockAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync("/api/v1/stock" + query);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    public static string Unique(int length) => Guid.NewGuid().ToString("N")[..length].ToUpperInvariant();

    // --- Pedidos, reservas y liberaciones por HTTP (T012, T014) ---

    public const string Orders = "/api/v1/customer-orders";
    public const string Reservations = "/api/v1/reservations";

    /// <summary>Alta de un pedido de una línea; devuelve pedido, línea y referencia externa.</summary>
    public static async Task<(Guid OrderId, Guid LineId, string Reference)> CreateOrderAsync(MasterDataApi session,
        Guid article, string quantity, string customer = "Cliente Norte")
    {
        var reference = "PED-" + Unique(8);
        using var created = await session.WriteAsync(HttpMethod.Post, Orders, new
        {
            externalReference = reference,
            customerReference = customer,
            lines = new[] { new { articleId = article, quantity } }
        });
        if (created.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Alta de pedido: {(int)created.StatusCode} {await created.Content.ReadAsStringAsync()}");
        var resource = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource");
        return (resource.GetProperty("id").GetGuid(), resource.GetProperty("lines")[0].GetProperty("id").GetGuid(), reference);
    }

    public static Task<(JsonElement Body, string ETag)> OrderAsync(HttpClient client, Guid id) =>
        VersionedAsync(client, $"{Orders}/{id:D}");

    public static Task<(JsonElement Body, string ETag)> ReservationAsync(HttpClient client, Guid id) =>
        VersionedAsync(client, $"{Reservations}/{id:D}");

    public static async Task<JsonElement> OrderContextAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<JsonElement>($"{Orders}/{id:D}/context")).Clone();

    public static Task<HttpResponseMessage> PostReservationAsync(MasterDataApi session, Guid line, string? etag,
        Guid? key, params (Guid Location, Guid? Lot, string Quantity)[] allocations) =>
        session.WriteAsync(HttpMethod.Post, Reservations, new
        {
            customerOrderLineId = line,
            allocations = allocations.Select(value => new { locationId = value.Location, lotId = value.Lot, quantity = value.Quantity })
        }, key, etag);

    public static Task<HttpResponseMessage> PostReleaseAsync(MasterDataApi session, Guid reservation, string? etag,
        Guid? key, Guid releaseId, string? reason, params (Guid Allocation, string Quantity)[] allocations) =>
        session.WriteAsync(HttpMethod.Post, $"{Reservations}/{reservation:D}/release", new
        {
            releaseId,
            reason,
            allocations = allocations.Select(value => new { allocationId = value.Allocation, quantity = value.Quantity })
        }, key, etag);

    /// <summary>Lote de un artículo por código (SIN-LOTE en artículos sin seguimiento).</summary>
    public async Task<Guid> LotAsync(Guid article, string code = Inventario.Api.Domain.Stock.Lot.UntrackedCode)
    {
        await using var database = fixture.ApplicationDatabase();
        return database.Set<Inventario.Api.Domain.Stock.Lot>().Single(lot => lot.ArticleId == article && lot.Code == code).Id;
    }

    /// <summary>
    /// Conciliación documental (INT-HU05-02, núcleo de S-07): por bucket del artículo en la
    /// ubicación, Σ RemainingQuantity de las asignaciones = StockBalances.Reserved, y el diario
    /// cuadra con el saldo.
    /// </summary>
    public async Task AssertCommitmentsMatchBalancesAsync(Guid article, Guid location)
    {
        await Stock.AssertJournalMatchesBalanceAsync(article, location);
        await using var database = fixture.ApplicationDatabase();
        var balances = await database.Set<Inventario.Api.Domain.Stock.StockBalance>().AsNoTracking()
            .Where(balance => balance.ArticleId == article && balance.LocationId == location).ToListAsync();
        var remaining = await database.Set<Inventario.Api.Domain.Stock.ReservationAllocation>().AsNoTracking()
            .Where(allocation => allocation.ArticleId == article && allocation.LocationId == location)
            .GroupBy(allocation => allocation.LotId)
            .Select(group => new { Lot = group.Key, Remaining = group.Sum(allocation => allocation.RemainingQuantity) })
            .ToListAsync();
        foreach (var balance in balances)
            Xunit.Assert.Equal(balance.Reserved, remaining.SingleOrDefault(value => value.Lot == balance.LotId)?.Remaining ?? 0m);
    }

    /// <summary>Desactiva el artículo por HTTP (GestorMaestros), con su versión actual.</summary>
    public static async Task DeactivateArticleAsync(MasterDataApi gestor, Guid article, string sku)
    {
        var send = await StockWriterAsync(StockWriter.ArticleDeactivate, article, sku, Guid.Empty, gestor, gestor);
        using var response = await send();
        Xunit.Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Desactiva una ubicación vacía por HTTP (Operario, Locations.Manage).</summary>
    public static async Task DeactivateLocationAsync(MasterDataApi operario, Guid location)
    {
        var send = await StockWriterAsync(StockWriter.LocationDeactivate, Guid.Empty, "", location, operario, operario);
        using var response = await send();
        Xunit.Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Escritor de otro agregado para las carreras de INT-HU05-01/02. Lee ahora la versión
    /// (If-Match) y devuelve la llamada para lanzarla después, como haría un usuario con la
    /// pantalla abierta.
    /// </summary>
    public static async Task<Func<Task<HttpResponseMessage>>> StockWriterAsync(StockWriter writer, Guid article, string sku,
        Guid location, MasterDataApi gestor, MasterDataApi operario)
    {
        if (writer == StockWriter.LocationDeactivate)
        {
            using var current = await operario.Client.GetAsync($"/api/v1/locations/{location:D}");
            var body = await current.Content.ReadFromJsonAsync<JsonElement>();
            var etag = current.Headers.ETag!.ToString();
            var write = new
            {
                code = body.GetProperty("code").GetString(), aisle = body.GetProperty("aisle").GetString(),
                rack = body.GetProperty("rack").GetString(), level = body.GetProperty("level").GetString(),
                kind = body.GetProperty("kind").GetString(), isActive = false
            };
            return () => operario.WriteAsync(HttpMethod.Put, $"/api/v1/locations/{location:D}", write, version: etag);
        }
        var path = $"/api/v1/articles/{article:D}";
        using var article_ = await gestor.Client.GetAsync(path);
        var family = (await article_.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("familyId").GetGuid();
        var version = article_.Headers.ETag!.ToString();
        return writer switch
        {
            StockWriter.ArticleDelete => () => gestor.WriteAsync(HttpMethod.Delete, path, new { }, version: version),
            StockWriter.ArticleDefinition => () => gestor.WriteAsync(HttpMethod.Put, path,
                MasterDataArticleTests.Body(family, sku, unit: "KG", fractions: true, lot: false), version: version),
            _ => () => gestor.WriteAsync(HttpMethod.Put, path,
                MasterDataArticleTests.Body(family, sku, lot: false, active: false), version: version)
        };
    }

    public static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var text = await response.Content.ReadAsStringAsync();
        Xunit.Assert.True(status == response.StatusCode, $"Se esperaba {(int)status} {code} y llegó {(int)response.StatusCode}: {text}");
        var problem = JsonDocument.Parse(text).RootElement.Clone();
        Xunit.Assert.Equal(code, problem.GetProperty("code").GetString());
        return problem;
    }

    private static async Task<(JsonElement Body, string ETag)> VersionedAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var text = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"GET {path}: {(int)response.StatusCode} {text}");
        return (JsonDocument.Parse(text).RootElement.Clone(), response.Headers.ETag!.ToString());
    }

    private async Task RunAsync(StockCommand command)
    {
        var (_, principal) = await Stock.ActorAsync();
        await Stock.RunAsync(principal, command);
    }

    public void Dispose()
    {
        foreach (var session in sessions) session.Dispose();
    }
}
