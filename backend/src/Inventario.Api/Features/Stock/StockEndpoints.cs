namespace Inventario.Api.Features.Stock;

/// <summary>
/// Registro de la feature de HU-05 (consultas de existencias, pedido de cliente mínimo y reservas).
/// T004 crea el punto de registro sin rutas; cada tarea posterior añade aquí sus servicios y rutas
/// (GET /stock en T006, pedidos en T008, reservas en T011 y T013).
/// </summary>
public static class StockEndpoints
{
    /// <summary>Servicios de HU-05. El contributor de uso de Article se registra en
    /// <c>AddStockCore</c>, junto al de stock, para que la cobertura de referencias de HU-08 no
    /// dependa del orden de registro (decisión de B, 2026-11-20).</summary>
    public static IServiceCollection AddStock(this IServiceCollection services) => services;

    /// <summary>Rutas de HU-05 bajo el grupo /api/v1. Sin rutas hasta T006.</summary>
    public static RouteGroupBuilder MapStockEndpoints(this RouteGroupBuilder api) => api;
}
