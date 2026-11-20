using System.Text.Json;
using Inventario.Api.Features.Locations;
using Inventario.Api.Features.Stock.CustomerOrders;
using Inventario.Api.Features.Stock.Queries;
using Inventario.Api.Features.Stock.Reservations;
using Inventario.Api.Infrastructure.Http;

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
        services.AddScoped<CustomerOrderCommands>();
        services.AddScoped<CustomerOrderQueries>();
        services.AddScoped<ICustomerOrderReader, CustomerOrderReader>();
        services.AddScoped<ReservationCommands>();
        services.AddScoped<ReservationQueries>();
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
        // T011 (D-12): compromisos por asignación con remanente; su total coincide con totals.reserved.
        stock.MapGet("/reservations", (string? articleId, string? locationId, string? lotId, int? page, int? pageSize,
                ReservationQueries queries, CancellationToken token) =>
                queries.CommitmentsAsync(articleId, locationId, lotId, page, pageSize, token))
            .RequireAuthorization(Read).Produces<ReservationCommitments>().ProducesProblem(404).ProducesProblem(422);

        // T008 (US2, FR-009–FR-013): alta y lectura del pedido de cliente mínimo. Sin PUT, PATCH,
        // DELETE ni cancelación (clarificación 3): esas rutas responden 404 o 405 como el resto.
        var orders = api.MapGroup("/customer-orders").WithTags("CustomerOrders");
        orders.MapPost("", (JsonElement body, HttpContext context, CustomerOrderCommands commands, CancellationToken token) =>
                commands.CreateAsync(LocationEndpoints.Key(context), CustomerOrderWrite.Read(body), token))
            .RequireAuthorization(CustomerOrderCommands.Permission).Accepts<CustomerOrderWrite>("application/json")
            .Produces<CustomerOrderMutation>(201).ProducesProblem(400).ProducesProblem(403).ProducesProblem(409).ProducesProblem(422);
        // Detalle versionado: solo datos propios y ETag de CustomerOrder.Version (D-07, D-11).
        orders.MapGet("/{id:guid}", async (Guid id, CustomerOrderQueries queries, HttpContext context, CancellationToken token) =>
            {
                var found = await queries.DetailAsync(id, token);
                if (found is null) return ApiProblems.Status(404);
                context.Response.Headers.ETag = EntityTags.Format(found.Value.Version);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(found.Value.View);
            })
            .RequireAuthorization(Read).Produces<CustomerOrderView>().ProducesProblem(404);
        // Contexto actual sin ETag, con orderVersion (patrón de GET /articles/{id}/context de HU-08).
        orders.MapGet("/{id:guid}/context", async (Guid id, CustomerOrderQueries queries, HttpContext context,
                CancellationToken token) =>
            {
                var found = await queries.ContextAsync(id, token);
                if (found is null) return ApiProblems.Status(404);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(found);
            })
            .RequireAuthorization(Read).Produces<CustomerOrderContext>().ProducesProblem(404);

        // T011 (US3, FR-014–FR-017): reservar con el If-Match del pedido; 201 sin ETag (D-08).
        var reservations = api.MapGroup("/reservations").WithTags("Reservations");
        reservations.MapPost("", (JsonElement body, HttpContext context, ReservationCommands commands, CancellationToken token) =>
                commands.ReserveAsync(LocationEndpoints.Key(context), ReservationWrite.Read(body),
                    context.Request.Headers.IfMatch.ToString(), token))
            .RequireAuthorization(ReservationCommands.Permission).Accepts<ReservationWrite>("application/json")
            .Produces<ReservationResult>(201).ProducesProblem(400).ProducesProblem(403).ProducesProblem(404)
            .ProducesProblem(409).ProducesProblem(412).ProducesProblem(422).ProducesProblem(428).ProducesProblem(503);
        // Detalle de la reserva: solo datos propios y ETag de Reservation.Version (D-07).
        reservations.MapGet("/{id:guid}", async (Guid id, ReservationQueries queries, HttpContext context, CancellationToken token) =>
            {
                var found = await queries.DetailAsync(id, token);
                if (found is null) return ApiProblems.Status(404);
                context.Response.Headers.ETag = EntityTags.Format(found.Value.Version);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(found.Value.View);
            })
            .RequireAuthorization(Read).Produces<ReservationView>().ProducesProblem(404);
        return api;
    }
}
