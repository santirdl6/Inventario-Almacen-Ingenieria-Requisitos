using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Features.Stock.Core;
using Inventario.IntegrationTests.Fixtures;

namespace Inventario.IntegrationTests.Stock;

/// <summary>
/// Soporte HTTP de las pruebas de HU-05 (T007). Abre sesión real con antiforgery por rol
/// (Operario, ResponsableAlmacen, GestorMaestros, Administrador) y prepara existencias solo con
/// StockCore, mediante <see cref="StockTestHarness"/> y sin modificarlo; nunca escribe saldos.
/// </summary>
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
