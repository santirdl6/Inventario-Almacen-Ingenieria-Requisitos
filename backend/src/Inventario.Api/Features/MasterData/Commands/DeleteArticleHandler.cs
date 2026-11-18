using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.MasterData.Commands;

public sealed class DeleteArticleHandler(InventoryOperationExecutor executor)
{
    public Task<OperationResponse> ExecuteAsync(Guid operation, Guid id, string? ifMatch, CancellationToken cancellation) =>
        executor.ExecuteAsync(new(operation, "DELETE", $"/api/v1/articles/{id:D}", "MasterData.Manage", JsonSerializer.SerializeToElement<object?>(null)), async (context, token) =>
        {
            await context.Locks.ArticlesAsync([id], token);
            var db = context.Database;
            var article = await db.Articles.SingleOrDefaultAsync(value => value.Id == id, token)
                ?? throw new InventoryProblemException(404, "NotFound", "El artículo ya no existe.");
            EntityTags.RequireMatch(ifMatch, article.Version);
            var usage = await context.Services.GetRequiredService<IArticleUsageReader>().ReadAsync(id, token);
            if (usage.HasHistory || usage.HasReferences)
                throw new InventoryProblemException(409, "HistoryPreventsDeletion", "Existen referencias o historial que impiden borrar el artículo. Puedes desactivarlo.");
            var before = ArticleDto.From(article);
            var policy = await db.Set<StockPolicy>().SingleOrDefaultAsync(value => value.ArticleId == id, token);
            if (policy is not null) db.Remove(policy);
            db.Remove(article);
            MasterDataAuditWriter.Record(context, MasterDataEntity.Article, id, MasterDataAction.Delete, before, null);
            return new OperationResponse(204, null, sourceDocumentId: id);
        }, cancellation);
}
