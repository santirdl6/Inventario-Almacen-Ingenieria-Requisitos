using Inventario.Api.Domain.MasterData;

namespace Inventario.Api.Infrastructure.Persistence;

// Las features consumidoras aportan consultas reales contra este mismo contexto.
public interface IArticleUsageContributor
{
    IReadOnlyCollection<Type> ReferencingEntityTypes { get; }
    Task<ArticleUsage> ReadAsync(InventoryDbContext database, Guid articleId, CancellationToken cancellationToken);
}

public sealed class ArticleUsageReader(InventoryDbContext database, ArticleUsageCoverageValidator coverage,
    IEnumerable<IArticleUsageContributor> contributors) : IArticleUsageReader
{
    public async Task<ArticleUsage> ReadAsync(Guid articleId, CancellationToken cancellationToken = default)
    {
        coverage.EnsureCovered();
        var result = new ArticleUsage();
        foreach (var contributor in contributors)
        {
            var part = await contributor.ReadAsync(database, articleId, cancellationToken);
            result = new(result.HasReferences || part.HasReferences || part.HasHistory, result.HasHistory || part.HasHistory);
        }
        // Vacío solo cuando el modelo no tiene consumidores, acreditado por coverage.
        return result;
    }
}
