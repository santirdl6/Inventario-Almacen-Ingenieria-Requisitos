using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Infrastructure.Persistence;

public sealed class ArticleReferenceTracker
{
    public async Task MarkAsync(OperationContext context, IEnumerable<Guid> articleIds, CancellationToken cancellation = default)
    {
        var ids = articleIds.Distinct().ToArray();
        await context.Locks.ArticlesAsync(ids, cancellation);
        var articles = await context.Database.Articles.Where(value => ids.Contains(value.Id)).ToListAsync(cancellation);
        if (articles.Count != ids.Length) throw new InventoryProblemException(422, "ValidationFailed", "Una referencia requiere artículos existentes.");
        foreach (var article in articles) article.LockDefinition();
        // El documento y la marca se guardan por el ejecutor en el mismo commit.
    }
}
