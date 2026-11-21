using System.Text.Json;
using Inventario.Api.Features.MasterData.Commands;
using Inventario.Api.Features.MasterData.Queries;
using Inventario.Api.Infrastructure.Http;

namespace Inventario.Api.Features.MasterData;

internal static class SupplierEndpoints
{
    public static void MapSupplierEndpoints(this RouteGroupBuilder api)
    {
        var suppliers = api.MapGroup("/suppliers").WithTags("Suppliers");
        suppliers.MapGet("", (string? q, string? state, int? page, int? pageSize, SupplierQueries queries, CancellationToken token) =>
            queries.ListAsync(MasterDataQueryOptions.Parse(q, state, page, pageSize), token)).RequireAuthorization("Inventory.Read");
        suppliers.MapGet("/{id}", async (Guid id, SupplierQueries queries, HttpContext context, CancellationToken token) =>
        {
            var supplier = await queries.DetailAsync(id, token);
            if (supplier is null) return ApiProblems.Status(404);
            context.Response.Headers.ETag = EntityTags.Format(supplier.Version);
            return Results.Ok(SupplierDto.From(supplier));
        }).RequireAuthorization("Inventory.Read").Produces<SupplierDto>().ProducesProblem(404);
        suppliers.MapPost("", (JsonElement body, HttpContext context, SupplierCommands commands, CancellationToken token) =>
            commands.WriteAsync(Key(context), null, SupplierWrite.Read(body, true), null, token)).RequireAuthorization("MasterData.Manage")
            .Accepts<SupplierWrite>("application/json").Produces<SupplierMutation>(201).ProducesProblem(400).ProducesProblem(409).ProducesProblem(422);
        suppliers.MapPut("/{id}", (Guid id, JsonElement body, HttpContext context, SupplierCommands commands, CancellationToken token) =>
            commands.WriteAsync(Key(context), id, SupplierWrite.Read(body, false), context.Request.Headers.IfMatch.ToString(), token)).RequireAuthorization("MasterData.Manage")
            .Accepts<SupplierWrite>("application/json").Produces<SupplierMutation>().ProducesProblem(400).ProducesProblem(409).ProducesProblem(412).ProducesProblem(422).ProducesProblem(428);
    }
    private static Guid Key(HttpContext context) => Guid.TryParse(context.Request.Headers["Idempotency-Key"].ToString(), out var key)
        && key != Guid.Empty ? key : throw new InventoryProblemException(400, "InvalidRequest", "Idempotency-Key debe ser un UUID no vacío.");
}
