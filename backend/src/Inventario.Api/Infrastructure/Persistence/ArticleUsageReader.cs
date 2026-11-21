using Inventario.Api.Domain.MasterData;

namespace Inventario.Api.Infrastructure.Persistence;

// Las features consumidoras aportan consultas reales contra este mismo contexto.
public interface IArticleUsageContributor
{
    IReadOnlyCollection<Type> ReferencingEntityTypes { get; }
    Task<ArticleUsage> ReadAsync(InventoryDbContext database, Guid articleId, CancellationToken cancellationToken);
}

// Una referencia derivada impide el borrado, pero no constituye primer uso de la definición.
public interface IDerivedArticleUsageContributor : IArticleUsageContributor;

public sealed class ArticleUsageReader(InventoryDbContext database, ArticleUsageCoverageValidator coverage,
    IEnumerable<IArticleUsageContributor> contributors) : IArticleUsageReader
{
    public Task<ArticleUsage> ReadAsync(Guid articleId, CancellationToken cancellationToken = default) => ReadCoreAsync(articleId, false, cancellationToken);

    public Task<ArticleUsage> ReadDefinitionAsync(Guid articleId, CancellationToken cancellationToken = default) => ReadCoreAsync(articleId, true, cancellationToken);

    private async Task<ArticleUsage> ReadCoreAsync(Guid articleId, bool definitionOnly, CancellationToken cancellationToken)
    {
        coverage.EnsureCovered();
        var result = new ArticleUsage();
        foreach (var contributor in contributors)
        {
            if (definitionOnly && contributor is IDerivedArticleUsageContributor) continue;
            var part = await contributor.ReadAsync(database, articleId, cancellationToken);
            result = new(result.HasReferences || part.HasReferences || part.HasHistory, result.HasHistory || part.HasHistory);
        }
        // Vacío solo cuando el modelo no tiene consumidores, acreditado por coverage.
        return result;
    }
}
