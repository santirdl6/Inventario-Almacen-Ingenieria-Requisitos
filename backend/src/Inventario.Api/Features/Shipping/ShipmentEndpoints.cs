using System.Text.Json;
using Inventario.Api.Features.Locations;
using Inventario.Api.Infrastructure.Http;

namespace Inventario.Api.Features.Shipping;

/// <summary>
/// Registro de la feature de salidas de HU-03 (research D-01). Rutas bajo /api/v1
/// (specs/005-registrar-salida-mercancia/contracts/shipping-api.md). Cada ruta declara su permiso
/// con RequireAuthorization; las escrituras lo vuelven a comprobar en el ejecutor común. El
/// contributor de uso de Article se registra en AddStockCore, no aquí (D-09).
/// </summary>
public static class ShipmentEndpoints
{
    private const string Read = "Inventory.Read";

    public static IServiceCollection AddShipping(this IServiceCollection services)
    {
        services.AddScoped<ShipmentCommands>();
        services.AddScoped<ShipmentQueries>();
        return services;
    }

    public static RouteGroupBuilder MapShippingEndpoints(this RouteGroupBuilder api)
    {
        var shipments = api.MapGroup("/shipments").WithTags("Shipments");
        // T007 (US1, FR-001–FR-005): borrador; crear, editar y eliminar con Idempotency-Key.
        shipments.MapPost("", (JsonElement body, HttpContext context, ShipmentCommands commands, CancellationToken token) =>
                commands.CreateAsync(LocationEndpoints.Key(context), ShipmentWrite.Read(body), token))
            .RequireAuthorization(ShipmentCommands.Permission).Accepts<ShipmentWrite>("application/json")
            .Produces<ShipmentMutation>(201).ProducesProblem(400).ProducesProblem(403).ProducesProblem(404)
            .ProducesProblem(409).ProducesProblem(422);
        // Detalle versionado: solo datos propios y ETag de Shipment.Version (D-05, D-12).
        shipments.MapGet("/{id:guid}", async (Guid id, ShipmentQueries queries, HttpContext context, CancellationToken token) =>
            {
                var found = await queries.DetailAsync(id, token);
                if (found is null) return ApiProblems.Status(404);
                context.Response.Headers.ETag = EntityTags.Format(found.Value.Version);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(found.Value.View);
            })
            .RequireAuthorization(Read).Produces<ShipmentView>().ProducesProblem(404);
        // T012 (US1.2, US4; FR-006, FR-018): contexto actual sin ETag con la revisión y lista del pedido.
        shipments.MapGet("/{id:guid}/context", async (Guid id, ShipmentQueries queries, HttpContext context,
                CancellationToken token) =>
            {
                var found = await queries.ContextAsync(id, token);
                if (found is null) return ApiProblems.Status(404);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(found);
            })
            .RequireAuthorization(Read).Produces<ShipmentContext>().ProducesProblem(404);
        shipments.MapGet("", async (string? customerOrderId, int? page, int? pageSize, ShipmentQueries queries,
                HttpContext context, CancellationToken token) =>
            {
                var found = await queries.ListAsync(customerOrderId, page, pageSize, token);
                if (found is null) return ApiProblems.Status(404);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(found);
            })
            .RequireAuthorization(Read).Produces<Inventario.Api.Features.MasterData.Queries.PageResult<ShipmentSummary>>()
            .ProducesProblem(404).ProducesProblem(422);
        shipments.MapPut("/{id:guid}", (Guid id, JsonElement body, HttpContext context, ShipmentCommands commands,
                CancellationToken token) =>
                commands.UpdateAsync(LocationEndpoints.Key(context), id, ShipmentWrite.Read(body),
                    context.Request.Headers.IfMatch.ToString(), token))
            .RequireAuthorization(ShipmentCommands.Permission).Accepts<ShipmentWrite>("application/json")
            .Produces<ShipmentMutation>().ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .ProducesProblem(412).ProducesProblem(422).ProducesProblem(428);
        shipments.MapDelete("/{id:guid}", (Guid id, HttpContext context, ShipmentCommands commands, CancellationToken token) =>
                commands.DeleteAsync(LocationEndpoints.Key(context), id, context.Request.Headers.IfMatch.ToString(), token))
            .RequireAuthorization(ShipmentCommands.Permission)
            .Produces(204).ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .ProducesProblem(412).ProducesProblem(428);
        // T009 (US1, US2; FR-007–FR-016): confirmar con If-Match de la salida y orderVersion; 200 sin ETag.
        shipments.MapPost("/{id:guid}/confirm", (Guid id, JsonElement body, HttpContext context, ShipmentCommands commands,
                CancellationToken token) =>
                commands.ConfirmAsync(LocationEndpoints.Key(context), id, ShipmentConfirmWrite.Read(body),
                    context.Request.Headers.IfMatch.ToString(), token))
            .RequireAuthorization(ShipmentCommands.Permission).Accepts<ShipmentConfirmWrite>("application/json")
            .Produces<ShipmentConfirmation>().ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .ProducesProblem(412).ProducesProblem(422).ProducesProblem(428).ProducesProblem(503);
        return api;
    }
}
