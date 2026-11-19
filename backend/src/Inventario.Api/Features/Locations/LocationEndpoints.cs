using System.Text.Json;
using Inventario.Api.Features.Locations.Commands;
using Inventario.Api.Features.Locations.Queries;
using Inventario.Api.Features.MasterData.Queries;
using Inventario.Api.Infrastructure.Http;

namespace Inventario.Api.Features.Locations;

// HU-02 · Persona B. Rutas bajo /api/v1 (specs/003-gestionar-ubicaciones/contracts/locations-api.md).
// Cada endpoint declara su permiso con RequireAuthorization y las mutaciones lo vuelven a
// comprobar en el InventoryOperationExecutor de A. No existe borrado de ubicaciones (FR-014).
public static class LocationEndpoints
{
    private const string Read = "Inventory.Read";

    public static IServiceCollection AddLocations(this IServiceCollection services)
    {
        services.AddScoped<ILocationUsage, StockBalanceLocationUsage>();
        services.AddScoped<LocationCommands>();
        services.AddScoped<LocationQueries>();
        return services;
    }

    public static RouteGroupBuilder MapLocationEndpoints(this RouteGroupBuilder api)
    {
        var locations = api.MapGroup("/locations").WithTags("Locations");
        locations.MapGet("", (string? search, string? kind, string? status, int? page, int? pageSize,
                LocationQueries queries, CancellationToken token) =>
                queries.ListAsync(search, kind, status, page, pageSize, token))
            .RequireAuthorization(Read).Produces<PageResult<LocationSummary>>().ProducesProblem(422);
        locations.MapGet("/{id:guid}", async (Guid id, LocationQueries queries, HttpContext context, CancellationToken token) =>
            {
                var found = await queries.DetailAsync(id, token);
                if (found is null) return ApiProblems.Status(404);
                context.Response.Headers.ETag = EntityTags.Format(found.Value.Version);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(found.Value.Detail);
            })
            .RequireAuthorization(Read).Produces<LocationDetail>().ProducesProblem(404);
        locations.MapPost("", (JsonElement body, HttpContext context, LocationCommands commands, CancellationToken token) =>
                commands.CreateAsync(Key(context), LocationWrite.Read(body, true), token))
            .RequireAuthorization(LocationCommands.Permission).Accepts<LocationWrite>("application/json")
            .Produces<LocationMutation>(201).ProducesProblem(400).ProducesProblem(403).ProducesProblem(409).ProducesProblem(422);
        locations.MapPut("/{id:guid}", (Guid id, JsonElement body, HttpContext context, LocationCommands commands,
                CancellationToken token) =>
                commands.UpdateAsync(Key(context), id, LocationWrite.Read(body, false), context.Request.Headers.IfMatch.ToString(), token))
            .RequireAuthorization(LocationCommands.Permission).Accepts<LocationWrite>("application/json")
            .Produces<LocationMutation>().ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .ProducesProblem(412).ProducesProblem(422).ProducesProblem(428);
        // FR-014: no existe borrado. Sin esta ruta, el fallback de /api respondería 404.
        locations.MapDelete("/{id:guid}", (HttpContext context) =>
            {
                context.Response.Headers.Allow = "GET, PUT";
                return ApiProblems.Status(405);
            })
            .RequireAuthorization().ExcludeFromDescription();
        locations.MapGet("/{id:guid}/stock", async (Guid id, int? page, int? pageSize, LocationQueries queries,
                CancellationToken token) =>
                await queries.StockAsync(id, page, pageSize, token) is { } result ? Results.Ok(result) : ApiProblems.Status(404))
            .RequireAuthorization(Read).Produces<LocationStock>().ProducesProblem(404).ProducesProblem(422);
        api.MapGet("/articles/{id:guid}/locations", async (Guid id, int? page, int? pageSize, bool? includeInactiveLocations,
                LocationQueries queries, CancellationToken token) =>
                await queries.ArticleLocationsAsync(id, page, pageSize, includeInactiveLocations, token) is { } result
                    ? Results.Ok(result) : ApiProblems.Status(404))
            .WithTags("Locations").RequireAuthorization(Read).Produces<ArticleLocations>().ProducesProblem(404).ProducesProblem(422);
        return api;
    }

    // Mismo contrato que los endpoints de maestros de A.
    internal static Guid Key(HttpContext context) =>
        Guid.TryParse(context.Request.Headers["Idempotency-Key"].ToString(), out var key) && key != Guid.Empty
            ? key : throw new InventoryProblemException(400, "InvalidRequest", "Idempotency-Key debe ser un UUID no vacío.");
}
