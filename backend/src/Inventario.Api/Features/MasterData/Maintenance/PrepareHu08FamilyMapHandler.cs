using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.MasterData.Maintenance;

public sealed class PrepareHu08FamilyMapHandler(InventoryOperationExecutor executor)
{
    public Task<OperationResponse> ExecuteAsync(Guid key, IReadOnlyList<FamilyMapEntry> entries, CancellationToken cancellation)
    {
        var ordered = entries.OrderBy(value => value.ArticleId.ToString("D"), StringComparer.Ordinal).ToArray();
        return executor.ExecuteAsync(new(key, "POST", "/api/v1/maintenance/hu08/family-map", "MasterData.Manage",
            JsonSerializer.SerializeToElement(new { entries = ordered }, OperationResponse.JsonOptions)), async (context, token) =>
        {
            var db = context.Database;
            if (!await ArticleSchema.IsExpandedAsync(db, token))
                throw new InventoryProblemException(503, "DependencyUnavailable", "La preparación solo se admite durante la expansión HU08.");
            if (ordered.Length is < 1 or > 100 || ordered.Select(value => value.ArticleId).Distinct().Count() != ordered.Length
                || ordered.Any(value => value.ArticleId == Guid.Empty || value.FamilyId == Guid.Empty))
                throw InventoryProblemException.Validation("entries", "El mapa admite 1–100 artículos distintos y familias válidas.");
            await context.Locks.DocumentsAsync(ordered.Select(value => new DocumentLock("family", value.FamilyId)), token);
            await context.Locks.ArticlesAsync(ordered.Select(value => value.ArticleId), token);
            for (var index = 0; index < ordered.Length; index++)
            {
                var entry = ordered[index];
                var family = await db.Set<Family>().AsNoTracking().SingleOrDefaultAsync(value => value.Id == entry.FamilyId, token);
                if (family?.IsActive != true)
                    throw InventoryProblemException.Validation($"entries[{index}].familyId", "La asignación requiere una familia activa.");
                var before = await db.Database.SqlQuery<Hu08FamilyPreparationSnapshot>($"""
                    SELECT Id, Sku, Name, FamilyId, BaseUnit, AllowsFractional, TracksLot, IsActive, IsDefinitionLocked
                    FROM dbo.Articles WHERE Id = {entry.ArticleId}
                    """).SingleOrDefaultAsync(token);
                if (before is null || before.FamilyId is not null && before.FamilyId != entry.FamilyId)
                    throw InventoryProblemException.Validation($"entries[{index}].articleId", "El artículo no existe o ya tiene otra asociación explícita.");
                var use = await context.Services.GetRequiredService<IArticleUsageReader>().ReadAsync(entry.ArticleId, token);
                var after = before with { FamilyId = entry.FamilyId, IsDefinitionLocked = before.IsDefinitionLocked || use.HasReferences || use.HasHistory };
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Articles SET FamilyId = {after.FamilyId}, IsDefinitionLocked = {after.IsDefinitionLocked} WHERE Id = {entry.ArticleId}", token);
                if (!await db.Set<StockPolicy>().AnyAsync(value => value.ArticleId == entry.ArticleId, token))
                    db.Set<StockPolicy>().Add(new StockPolicy(entry.ArticleId));
                MasterDataAuditWriter.Record(context, MasterDataEntity.Article, entry.ArticleId, MasterDataAction.Update, before, after);
            }
            return OperationResponse.Json(200, new { operationId = key, preparedArticleIds = ordered.Select(value => value.ArticleId) });
        }, cancellation);
    }
}
