using System.Text.Json;
using Inventario.Api.Features.MasterData.Commands;
using Inventario.Api.Features.MasterData.Queries;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Features.MasterData.Maintenance;
using Inventario.Api.Infrastructure.Persistence;

namespace Inventario.Api.Features.MasterData;

internal static class ArticleEndpoints
{
    public static void MapArticleEndpoints(this RouteGroupBuilder api)
    {
        var articles = api.MapGroup("/articles").WithTags("Articles").AddEndpointFilter(async (context, next) =>
        {
            if (!await ArticleSchema.IsFinalAsync(context.HttpContext.RequestServices.GetRequiredService<InventoryDbContext>(), context.HttpContext.RequestAborted))
                throw new InventoryProblemException(503, "DependencyUnavailable", "Completa la transición de familia antes de usar artículos.");
            return await next(context);
        });
        articles.MapGet("", (string? q, string? state, Guid? familyId, string? skuPrefix, int? page, int? pageSize, ArticleQueries queries, CancellationToken token) =>
            queries.ListAsync(MasterDataQueryOptions.Parse(q, state, page, pageSize), familyId, token, skuPrefix)).RequireAuthorization("Inventory.Read");
        articles.MapGet("/{id}", async (Guid id, ArticleQueries queries, HttpContext context, CancellationToken token) =>
        {
            var article = await queries.DetailAsync(id, token);
            if (article is null) return ApiProblems.Status(404);
            context.Response.Headers.ETag = EntityTags.Format(article.Version);
            return Results.Ok(ArticleDto.From(article));
        }).RequireAuthorization("Inventory.Read").Produces<ArticleDto>();
        articles.MapGet("/{id}/context", (Guid id, ArticleQueries queries, CancellationToken token) => queries.ContextAsync(id, token))
            .RequireAuthorization("Inventory.Read").Produces<ArticleContextDto>().ProducesProblem(404).ProducesProblem(503);
        articles.MapPost("", (JsonElement body, HttpContext context, ArticleCommands commands, CancellationToken token) =>
            commands.WriteAsync(Key(context), null, ArticleWrite.Read(body, true), null, token)).RequireAuthorization("MasterData.Manage")
            .Accepts<ArticleWrite>("application/json").Produces<ArticleMutation>(201);
        articles.MapPut("/{id}", (Guid id, JsonElement body, HttpContext context, ArticleCommands commands, CancellationToken token) =>
            commands.WriteAsync(Key(context), id, ArticleWrite.Read(body, false), context.Request.Headers.IfMatch.ToString(), token))
            .RequireAuthorization("MasterData.Manage").Accepts<ArticleWrite>("application/json").Produces<ArticleMutation>();
        articles.MapDelete("/{id}", (Guid id, HttpContext context, DeleteArticleHandler handler, CancellationToken token) =>
            handler.ExecuteAsync(Key(context), id, context.Request.Headers.IfMatch.ToString(), token))
            .RequireAuthorization("MasterData.Manage").Produces(204).ProducesProblem(404).ProducesProblem(409).ProducesProblem(412).ProducesProblem(428).ProducesProblem(503);
        articles.MapGet("/{id}/movements", (Guid id, string? from, string? to, Guid? actorId, Guid? locationId, Guid? lotId,
            int? page, int? pageSize, ArticleMovementQueries queries, CancellationToken token) =>
                queries.ReadAsync(id, ArticleMovementOptions.Parse(from, to, actorId, locationId, lotId, page, pageSize), token))
            .RequireAuthorization("Inventory.Read").Produces<PageResult<ArticleMovementDto>>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(422).ProducesProblem(503);
        articles.MapGet("/{id}/movements/filters", (Guid id, string field, string? q, int? page, int? pageSize,
            ArticleMovementQueries queries, CancellationToken token) =>
                queries.FiltersAsync(id, field, MasterDataQueryOptions.Parse(q, null, page, pageSize), token))
            .RequireAuthorization("Inventory.Read").Produces<PageResult<MovementFilterOption>>()
            .ProducesProblem(404).ProducesProblem(422).ProducesProblem(503);
    }
    private static Guid Key(HttpContext context) => Guid.TryParse(context.Request.Headers["Idempotency-Key"].ToString(), out var key)
        && key != Guid.Empty ? key : throw new InventoryProblemException(400, "InvalidRequest", "Idempotency-Key debe ser un UUID no vacío.");
}
