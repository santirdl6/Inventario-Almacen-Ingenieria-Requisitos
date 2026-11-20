using Inventario.Api.Features.Stock.Queries;

namespace Inventario.Api.Features.Stock;

/// <summary>
/// Registro de la feature de HU-05 (consultas de existencias, pedido de cliente mínimo y reservas).
/// Rutas bajo /api/v1 (specs/004-consultar-existencias/contracts/stock-api.md). Cada ruta declara su
/// permiso con RequireAuthorization; las mutaciones lo vuelven a comprobar en el ejecutor común.
/// </summary>
public static class StockEndpoints
{
    private const string Read = "Inventory.Read";

    /// <summary>Servicios de HU-05. El contributor de uso de Article se registra en
    /// <c>AddStockCore</c>, junto al de stock, para que la cobertura de referencias de HU-08 no
    /// dependa del orden de registro (decisión de B, 2026-11-20).</summary>
    public static IServiceCollection AddStock(this IServiceCollection services)
    {
        services.AddScoped<StockBalanceReader>();
        services.AddScoped<StockQueries>();
        return services;
    }

    /// <summary>Rutas de HU-05 bajo el grupo /api/v1.</summary>
    public static RouteGroupBuilder MapStockEndpoints(this RouteGroupBuilder api)
    {
        var stock = api.MapGroup("/stock").WithTags("Stock");
        // T006 (US1, FR-001–FR-008): existencias de un artículo, con totales del filtro completo.
        stock.MapGet("", (string? articleId, string? locationId, string? lotId, string? includeEmpty, int? page,
                int? pageSize, StockQueries queries, CancellationToken token) =>
                queries.StockAsync(articleId, locationId, lotId, includeEmpty, page, pageSize, token))
            .RequireAuthorization(Read).Produces<StockQueryResult>().ProducesProblem(404).ProducesProblem(422);
        return api;
    }
}
