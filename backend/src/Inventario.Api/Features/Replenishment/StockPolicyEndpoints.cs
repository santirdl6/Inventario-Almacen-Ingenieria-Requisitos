using System.Globalization;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Replenishment;

public static class StockPolicyEndpoints
{
    public static void MapStockPolicyEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/articles/{id:guid}/stock-policy", async (Guid id, InventoryDbContext db, HttpContext http, CancellationToken token) =>
        {
            var policy = await db.Set<StockPolicy>().AsNoTracking().SingleOrDefaultAsync(x => x.ArticleId == id, token);
            if (policy is null) return ApiProblems.Status(404);
            http.Response.Headers.ETag = EntityTags.Format(policy.Version);
            return Results.Ok(ToDto(policy));
        }).RequireAuthorization("Inventory.Read");
        api.MapPut("/articles/{id:guid}/stock-policy", (Guid id, JsonElement body, HttpContext http, InventoryOperationExecutor executor, CancellationToken token) =>
        {
            if (!Guid.TryParse(http.Request.Headers["Idempotency-Key"], out var key) || key == Guid.Empty)
                throw new InventoryProblemException(400, "InvalidRequest", "Idempotency-Key debe ser un UUID no vacío.");
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("minimumAvailable", out var field) || field.ValueKind != JsonValueKind.String
                || !decimal.TryParse(field.GetString(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
                || value < 0 || value >= 1_000_000_000_000_000m || decimal.Round(value, 3) != value)
                throw InventoryProblemException.Validation("minimumAvailable", "Indica un mínimo no negativo de hasta tres decimales.");
            var normalized = JsonSerializer.SerializeToElement(new { minimumAvailable = value.ToString("F3", CultureInfo.InvariantCulture) });
            var etag = http.Request.Headers.IfMatch.ToString();
            return executor.ExecuteAsync(new(key, "PUT", $"/api/v1/articles/{id:D}/stock-policy", "StockPolicy.Manage", normalized), async (context, ct) =>
            {
                await context.Locks.ArticlesAsync([id], ct);
                var article = await context.Database.Articles.SingleOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw new InventoryProblemException(404, "NotFound", "El artículo no existe.");
                var policy = await context.Database.Set<StockPolicy>().SingleOrDefaultAsync(x => x.ArticleId == id, ct)
                    ?? throw new InventoryProblemException(503, "DependencyUnavailable", "Falta la política de existencias.");
                EntityTags.RequireMatch(etag, policy.Version);
                if (!article.IsActive) throw InventoryProblemException.Validation("articleId", "El artículo está inactivo.");
                if (!article.AllowsFractional && decimal.Truncate(value) != value)
                    throw InventoryProblemException.Validation("minimumAvailable", "El artículo requiere unidades enteras.");
                var before = ToDto(policy);
                policy.SetMinimum(value);
                context.Database.Entry(policy).Property(x => x.MinimumAvailable).IsModified = true;
                await context.Services.GetRequiredService<StockAlertEvaluator>().EvaluateAsync(context, [id], ct);
                await context.Database.SaveChangesAsync(ct);
                var after = ToDto(policy);
                MasterDataAuditWriter.Record(context, MasterDataEntity.Article, id, MasterDataAction.Update, before, after);
                return OperationResponse.Json(200, new { operationId = key, resource = after }, id,
                    new Dictionary<string, string> { ["ETag"] = EntityTags.Format(policy.Version) });
            }, token);
        }).RequireAuthorization("StockPolicy.Manage");
    }

    private static object ToDto(StockPolicy policy) => new { articleId = policy.ArticleId,
        minimumAvailable = policy.MinimumAvailable.ToString("F3", CultureInfo.InvariantCulture), version = Convert.ToBase64String(policy.Version) };
}
