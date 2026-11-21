using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Stock.Core;

internal sealed class StockAlertArticleUsage : IDerivedArticleUsageContributor
{
    private bool? schemaApplied;
    public IReadOnlyCollection<Type> ReferencingEntityTypes { get; } = [typeof(StockAlert)];

    public async Task<ArticleUsage> ReadAsync(InventoryDbContext database, Guid articleId, CancellationToken cancellationToken)
    {
        schemaApplied ??= (await database.Database.GetAppliedMigrationsAsync(cancellationToken)).Contains("20261121200510_HU06StockAlerts");
        if (schemaApplied != true) return new ArticleUsage(false, false);
        var exists = await database.Set<StockAlert>().AnyAsync(alert => alert.ArticleId == articleId, cancellationToken);
        return new ArticleUsage(exists, false);
    }
}
