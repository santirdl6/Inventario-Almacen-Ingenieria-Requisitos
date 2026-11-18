using System.Text.Json;
using Inventario.Api.Features.MasterData.Commands;
using Inventario.Api.Features.MasterData.Queries;
using Inventario.Api.Infrastructure.Http;

namespace Inventario.Api.Features.MasterData;

internal static class FamilyEndpoints
{
    public static void MapFamilyEndpoints(this RouteGroupBuilder api)
    {
        var families = api.MapGroup("/families").WithTags("Families");
        families.MapGet("", (string? q, string? state, int? page, int? pageSize, FamilyQueries queries, CancellationToken token) =>
            queries.ListAsync(MasterDataQueryOptions.Parse(q, state, page, pageSize), token)).RequireAuthorization("Inventory.Read");
        families.MapGet("/{id}", async (Guid id, FamilyQueries queries, HttpContext context, CancellationToken token) =>
        {
            var family = await queries.DetailAsync(id, token);
            if (family is null) return ApiProblems.Status(404);
            context.Response.Headers.ETag = EntityTags.Format(family.Version);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(FamilyDto.From(family));
        }).RequireAuthorization("Inventory.Read").Produces<FamilyDto>().ProducesProblem(404);
        families.MapPost("", (JsonElement body, HttpContext context, FamilyCommands commands, CancellationToken token) =>
            commands.WriteAsync(Key(context), null, FamilyWrite.Read(body, true), null, token))
            .RequireAuthorization("MasterData.Manage").Accepts<FamilyWrite>("application/json")
            .Produces<FamilyMutation>(201).ProducesProblem(400).ProducesProblem(409).ProducesProblem(422);
        families.MapPut("/{id}", (Guid id, JsonElement body, HttpContext context, FamilyCommands commands, CancellationToken token) =>
            commands.WriteAsync(Key(context), id, FamilyWrite.Read(body, false), context.Request.Headers.IfMatch.ToString(), token))
            .RequireAuthorization("MasterData.Manage").Accepts<FamilyWrite>("application/json")
            .Produces<FamilyMutation>().ProducesProblem(400).ProducesProblem(409).ProducesProblem(412).ProducesProblem(422).ProducesProblem(428);
    }

    private static Guid Key(HttpContext context) => Guid.TryParse(context.Request.Headers["Idempotency-Key"].ToString(), out var key)
        && key != Guid.Empty ? key : throw new InventoryProblemException(400, "InvalidRequest", "Idempotency-Key debe ser un UUID no vacío.");
}
