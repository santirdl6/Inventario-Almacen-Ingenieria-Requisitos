using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Infrastructure.Persistence;

internal sealed class PurchasingArticleUsageContributor : IArticleUsageContributor, IArticleFirstReferenceContributor
{
    private bool? schemaApplied;
    public IReadOnlyCollection<Type> ReferencingEntityTypes { get; } = [typeof(PurchaseOrderLine)];

    public async Task<ArticleUsage> ReadAsync(InventoryDbContext database, Guid articleId, CancellationToken cancellationToken)
    {
        // Explicit historical maintenance windows can precede the additive migration.
        // SQL failures are never converted into a claim that no reference exists.
        schemaApplied ??= (await database.Database.GetAppliedMigrationsAsync(cancellationToken))
            .Any(name => name.EndsWith("_HU07PurchasingOutbox", StringComparison.Ordinal));
        if (schemaApplied != true) return new ArticleUsage(false, false);
        return new ArticleUsage(await database.Set<PurchaseOrderLine>()
            .AnyAsync(line => line.ArticleId == articleId, cancellationToken), false);
    }
}
