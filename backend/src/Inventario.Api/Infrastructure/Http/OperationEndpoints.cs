using System.Security.Claims;
using System.Text.Json;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Infrastructure.Http;

public sealed record OperationOutcome(Guid OperationId, string Method, string Resource, int Status, JsonElement? Body,
    IReadOnlyDictionary<string, string> Headers, DateTimeOffset RecordedAt);

internal static class OperationEndpoints
{
    public static void MapOperationEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/operations/{operationId}", async (Guid operationId, InventoryDbContext database,
            ClaimsPrincipal principal, IAuthorizationService authorization, HttpContext context, CancellationToken cancellation) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var result = await database.OperationResults.AsNoTracking()
                .SingleOrDefaultAsync(value => value.OperationId == operationId, cancellation);
            if (result is null) return ApiProblems.Status(404);
            if (!(await authorization.AuthorizeAsync(principal, null, result.RequiredPermission)).Succeeded)
                return ApiProblems.Status(403);
            var response = result.Response();
            JsonElement? body = response.Body is null ? null : JsonSerializer.Deserialize<JsonElement>(response.Body);
            return Results.Ok(new OperationOutcome(result.OperationId, result.Method, result.Resource, result.ResultStatus, body, response.Headers, result.RecordedAt));
        }).RequireAuthorization().WithTags("Operations").WithSummary("Consulta el resultado confirmado de una intención autorizada")
            .Produces<OperationOutcome>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
    }
}
