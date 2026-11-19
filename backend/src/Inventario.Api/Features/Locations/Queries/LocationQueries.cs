using System.Data;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.MasterData.Queries;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Locations.Queries;

// Consultas de HU-02 (contracts/locations-api.md). Solo lectura: AsNoTracking y, para las
// existencias, StockBalances como única fuente (D-09).
public sealed class LocationQueries(InventoryDbContext database, ILocationUsage usage)
{
    public async Task<PageResult<LocationSummary>> ListAsync(string? search, string? kind, string? status,
        int? page, int? pageSize, CancellationToken cancellation)
    {
        var paging = Paging.From(page, pageSize);
        var query = database.Set<Location>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var prefix = search.Trim().ToUpperInvariant();
            query = query.Where(location => location.Code.StartsWith(prefix) || location.Aisle.StartsWith(prefix));
        }
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!Enum.TryParse<LocationKind>(kind.Trim(), true, out var parsed) || !Enum.IsDefined(parsed) || int.TryParse(kind, out _))
                throw InventoryProblemException.Validation("kind", "El tipo debe ser Storage o Receiving.");
            query = query.Where(location => location.Kind == parsed);
        }
        query = (status?.Trim().ToLowerInvariant() ?? "active") switch
        {
            "active" or "" => query.Where(location => location.IsActive),
            "inactive" => query.Where(location => !location.IsActive),
            "all" => query,
            _ => throw InventoryProblemException.Validation("status", "El estado debe ser active, inactive o all.")
        };
        var total = await query.CountAsync(cancellation);
        var items = await query.OrderBy(location => location.Code).ThenBy(location => location.Id)
            .Skip(paging.Skip).Take(paging.PageSize).ToListAsync(cancellation);
        return new(items.Select(LocationSummary.From).ToList(), paging.Page, paging.PageSize, total);
    }

    public async Task<(LocationDetail Detail, byte[] Version)?> DetailAsync(Guid id, CancellationToken cancellation)
    {
        var location = await database.Set<Location>().AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellation);
        return location is null ? null : (await DetailAsync(database, usage, location, cancellation), location.Version);
    }

    /// <summary>Detalle con el uso de la ubicación; los comandos lo llaman con su propio contexto.</summary>
    public static async Task<LocationDetail> DetailAsync(InventoryDbContext database, ILocationUsage usage,
        Location location, CancellationToken cancellation)
    {
        var hasMovements = await usage.HasMovementsAsync(location.Id, cancellation);
        var totals = await usage.GetTotalsAsync(location.Id, cancellation);
        var inOpenCount = await usage.IsInOpenCountAsync(location.Id, cancellation);
        var lastReceiving = location is { Kind: LocationKind.Receiving, IsActive: true }
            && await ActiveReceivingCountAsync(database, cancellation) == 1;
        return LocationDetail.From(location, hasMovements, totals, inOpenCount, lastReceiving);
    }

    /// <summary>US2: todas las ubicaciones de un artículo, por ubicación y lote, desde StockBalances.
    /// Página y totales en la misma instantánea SNAPSHOT; los totales cubren todo el artículo.</summary>
    public Task<ArticleLocations?> ArticleLocationsAsync(Guid articleId, int? page, int? pageSize,
        bool? includeInactiveLocations, CancellationToken cancellation)
    {
        var paging = Paging.From(page, pageSize);
        return database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
            var article = await database.Set<Article>().AsNoTracking().SingleOrDefaultAsync(value => value.Id == articleId, cancellation);
            if (article is null) return null;
            var asOf = DateTimeOffset.UtcNow;
            var rows = from balance in database.Set<StockBalance>().AsNoTracking()
                       join location in database.Set<Location>().AsNoTracking() on balance.LocationId equals location.Id
                       join lot in database.Set<Lot>().AsNoTracking() on balance.LotId equals lot.Id
                       where balance.ArticleId == articleId && (balance.Physical != 0m || balance.Reserved != 0m)
                       select new { balance.Physical, balance.Reserved, Location = location, LotCode = lot.Code };
            if (includeInactiveLocations == false) rows = rows.Where(row => row.Location.IsActive);
            var totals = await rows.GroupBy(row => 1)
                .Select(group => new { Physical = group.Sum(row => row.Physical), Reserved = group.Sum(row => row.Reserved) })
                .SingleOrDefaultAsync(cancellation);
            var count = await rows.CountAsync(cancellation);
            var items = await rows.OrderBy(row => row.Location.Code).ThenBy(row => row.LotCode)
                .Skip(paging.Skip).Take(paging.PageSize).ToListAsync(cancellation);
            await transaction.CommitAsync(cancellation);
            return new ArticleLocations(asOf, Ref(article),
                new QuantityTotals(LocationText.Quantity(totals?.Physical ?? 0m), LocationText.Quantity(totals?.Reserved ?? 0m)),
                items.Select(row => new ArticleLocationBucket(LocationText.Ref(row.Location), row.LotCode,
                    LocationText.Quantity(row.Physical), LocationText.Quantity(row.Reserved))).ToList(),
                paging.Page, paging.PageSize, count);
        });
    }

    internal static ArticleRef Ref(Article article) => new(article.Id, article.Sku, article.Name, article.BaseUnit, article.IsActive);

    public static Task<int> ActiveReceivingCountAsync(InventoryDbContext database, CancellationToken cancellation) =>
        database.Set<Location>().AsNoTracking()
            .CountAsync(location => location.Kind == LocationKind.Receiving && location.IsActive, cancellation);
}
