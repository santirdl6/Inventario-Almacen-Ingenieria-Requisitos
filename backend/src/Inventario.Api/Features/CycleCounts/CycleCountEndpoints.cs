using System.Text.Json;
using Inventario.Api.Features.Locations;
using Inventario.Api.Features.MasterData.Queries;
using Inventario.Api.Infrastructure.Http;

namespace Inventario.Api.Features.CycleCounts;

/// <summary>
/// Registro de la feature de conteos de HU-04 (research D-01). Rutas bajo /api/v1
/// (specs/006-realizar-inventarios-ciclicos/contracts/cycle-counts-api.md). Cada ruta declara su permiso
/// con RequireAuthorization; las escrituras lo vuelven a comprobar en el ejecutor común. El contributor
/// de uso de Article se registra en AddStockCore, no aquí (D-08).
/// </summary>
public static class CycleCountEndpoints
{
    private const string Read = "Inventory.Read";

    public static IServiceCollection AddCycleCounts(this IServiceCollection services)
    {
        services.AddScoped<CycleCountCommands>();
        services.AddScoped<CycleCountQueries>();
        return services;
    }

    public static RouteGroupBuilder MapCycleCountEndpoints(this RouteGroupBuilder api)
    {
        var counts = api.MapGroup("/cycle-counts").WithTags("CycleCounts");
        // T006 (US1.1, FR-001): borrador; crear y editar el alcance con Idempotency-Key.
        counts.MapPost("", (JsonElement body, HttpContext context, CycleCountCommands commands, CancellationToken token) =>
                commands.CreateAsync(LocationEndpoints.Key(context), CycleCountWrite.Read(body), token))
            .RequireAuthorization(CycleCountCommands.RecordPermission).Accepts<CycleCountWrite>("application/json")
            .Produces<CycleCountMutation>(201).ProducesProblem(400).ProducesProblem(403).ProducesProblem(409).ProducesProblem(422);
        counts.MapPut("/{id:guid}", (Guid id, JsonElement body, HttpContext context, CycleCountCommands commands,
                CancellationToken token) =>
                commands.UpdateAsync(LocationEndpoints.Key(context), id, CycleCountWrite.Read(body),
                    context.Request.Headers.IfMatch.ToString(), token))
            .RequireAuthorization(CycleCountCommands.RecordPermission).Accepts<CycleCountWrite>("application/json")
            .Produces<CycleCountMutation>().ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .ProducesProblem(412).ProducesProblem(422).ProducesProblem(428);

        // T007 (US1.2, FR-002/FR-003): iniciar congela el alcance y captura el teórico.
        counts.MapPost("/{id:guid}/start", (Guid id, HttpContext context, CycleCountCommands commands, CancellationToken token) =>
                commands.StartAsync(LocationEndpoints.Key(context), id, context.Request.Headers.IfMatch.ToString(), token))
            .RequireAuthorization(CycleCountCommands.RecordPermission)
            .Produces<CycleCountMutation>().ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .ProducesProblem(412).ProducesProblem(422).ProducesProblem(428).ProducesProblem(503);

        // T008 (US1.3–US1.5, US5; FR-004–FR-007): contar, hallazgos, retirar hallazgos y enviar.
        counts.MapPut("/{id:guid}/lines", (Guid id, JsonElement body, HttpContext context, CycleCountCommands commands,
                CancellationToken token) =>
                commands.RecordLinesAsync(LocationEndpoints.Key(context), id, CycleCountLinesWrite.Read(body),
                    context.Request.Headers.IfMatch.ToString(), token))
            .RequireAuthorization(CycleCountCommands.RecordPermission).Accepts<CycleCountLinesWrite>("application/json")
            .Produces<CycleCountMutation>().ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .ProducesProblem(412).ProducesProblem(422).ProducesProblem(428);
        counts.MapDelete("/{id:guid}/lines/{lineId:guid}", (Guid id, Guid lineId, HttpContext context, CycleCountCommands commands,
                CancellationToken token) =>
                commands.RemoveLineAsync(LocationEndpoints.Key(context), id, lineId, context.Request.Headers.IfMatch.ToString(), token))
            .RequireAuthorization(CycleCountCommands.RecordPermission)
            .Produces(204).ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .ProducesProblem(412).ProducesProblem(422).ProducesProblem(428);
        counts.MapPost("/{id:guid}/submit", (Guid id, HttpContext context, CycleCountCommands commands, CancellationToken token) =>
                commands.SubmitAsync(LocationEndpoints.Key(context), id, context.Request.Headers.IfMatch.ToString(), token))
            .RequireAuthorization(CycleCountCommands.RecordPermission)
            .Produces<CycleCountMutation>().ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .ProducesProblem(412).ProducesProblem(422).ProducesProblem(428);

        // Lecturas (D-12): detalle versionado, contexto sin ETag y lista paginada.
        counts.MapGet("", async (string? state, int? page, int? pageSize, CycleCountQueries queries, HttpContext context,
                CancellationToken token) =>
            {
                var found = await queries.ListAsync(state, page, pageSize, token);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(found);
            })
            .RequireAuthorization(Read).Produces<PageResult<CycleCountSummary>>().ProducesProblem(422);
        counts.MapGet("/{id:guid}", async (Guid id, CycleCountQueries queries, HttpContext context, CancellationToken token) =>
            {
                var found = await queries.DetailAsync(id, token);
                if (found is null) return ApiProblems.Status(404);
                context.Response.Headers.ETag = EntityTags.Format(found.Value.Version);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(found.Value.View);
            })
            .RequireAuthorization(Read).Produces<CycleCountView>().ProducesProblem(404);
        counts.MapGet("/{id:guid}/context", async (Guid id, CycleCountQueries queries, HttpContext context, CancellationToken token) =>
            {
                var found = await queries.ContextAsync(id, token);
                if (found is null) return ApiProblems.Status(404);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(found);
            })
            .RequireAuthorization(Read).Produces<CycleCountContext>().ProducesProblem(404);
        return api;
    }
}
