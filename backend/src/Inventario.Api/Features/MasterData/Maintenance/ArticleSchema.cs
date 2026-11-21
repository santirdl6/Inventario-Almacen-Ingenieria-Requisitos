using Microsoft.EntityFrameworkCore;
using Inventario.Api.Infrastructure.Persistence;

namespace Inventario.Api.Features.MasterData.Maintenance;

public static class ArticleSchema
{
    public const string Expand = "20261117095524_HU08MasterDataExpand";
    public const string Final = "20261117095527_HU08RequireArticleFamily";
    public static async Task<bool> IsFinalAsync(InventoryDbContext db, CancellationToken cancellation = default) =>
        (await db.Database.GetAppliedMigrationsAsync(cancellation)).Contains(Final);
    public static async Task<bool> IsExpandedAsync(InventoryDbContext db, CancellationToken cancellation = default)
    {
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellation)).ToHashSet();
        return applied.Contains(Expand) && !applied.Contains(Final);
    }
}
