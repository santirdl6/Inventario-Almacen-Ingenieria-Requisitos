using Inventario.Api.Infrastructure.Http;

namespace Inventario.Api.Features.Locations;

// HU-02 · Persona B. Rutas bajo /api/v1 (specs/003-gestionar-ubicaciones/contracts/locations-api.md).
// Cada endpoint declara su permiso con RequireAuthorization y las mutaciones lo vuelven a
// comprobar en el InventoryOperationExecutor de A. No existe borrado de ubicaciones (FR-014).
public static class LocationEndpoints
{
    public static IServiceCollection AddLocations(this IServiceCollection services)
    {
        services.AddScoped<ILocationUsage, StockBalanceLocationUsage>();
        return services;
    }

    public static RouteGroupBuilder MapLocationEndpoints(this RouteGroupBuilder api)
    {
        api.MapGroup("/locations").WithTags("Locations");
        return api;
    }

    // Mismo contrato que los endpoints de maestros de A.
    internal static Guid Key(HttpContext context) =>
        Guid.TryParse(context.Request.Headers["Idempotency-Key"].ToString(), out var key) && key != Guid.Empty
            ? key : throw new InventoryProblemException(400, "InvalidRequest", "Idempotency-Key debe ser un UUID no vacío.");
}
