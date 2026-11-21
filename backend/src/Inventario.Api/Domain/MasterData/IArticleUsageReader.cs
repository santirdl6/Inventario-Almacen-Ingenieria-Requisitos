namespace Inventario.Api.Domain.MasterData;

public readonly record struct ArticleUsage(bool HasReferences, bool HasHistory);

public interface IArticleUsageReader
{
    Task<ArticleUsage> ReadAsync(Guid articleId, CancellationToken cancellationToken = default);
    Task<ArticleUsage> ReadDefinitionAsync(Guid articleId, CancellationToken cancellationToken = default) => ReadAsync(articleId, cancellationToken);
}
