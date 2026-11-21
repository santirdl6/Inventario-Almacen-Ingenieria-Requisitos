namespace Inventario.Api.Features.Shipping;

/// <summary>
/// HU-03 · Registro de la feature de salidas (research D-01). T004 solo crea el punto de registro;
/// T005 lo conecta en Program.cs y las tareas siguientes añaden servicios y rutas.
/// </summary>
public static class ShipmentEndpoints
{
    public static IServiceCollection AddShipping(this IServiceCollection services) => services;

    public static RouteGroupBuilder MapShippingEndpoints(this RouteGroupBuilder api) => api;
}
