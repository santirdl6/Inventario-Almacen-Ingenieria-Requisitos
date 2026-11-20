using System.Data;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.MasterData.Queries;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.Api.Features.Stock.Queries;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Locations.Queries;

// Consultas de HU-02 (contracts/locations-api.md). Solo lectura: AsNoTracking y, para las
// existencias, StockBalances como única fuente (D-09).
public sealed class LocationQueries(InventoryDbContext database, ILocationUsage usage, StockBalanceReader reader)
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

    public Task<(LocationDetail Detail, byte[] Version)?> DetailAsync(Guid id, CancellationToken cancellation) =>
        database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
            var location = await database.Set<Location>().AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellation);
            if (location is null) return ((LocationDetail Detail, byte[] Version)?)null;
            var detail = await DetailAsync(database, usage, location, cancellation);
            await transaction.CommitAsync(cancellation);
            return (detail, LocationEntityTags.Token(detail, location.Version));
        });

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
    /// Delega en el agregado único de HU-05 (StockBalanceReader, research D-02; T020): página y
    /// totales en la misma instantánea SNAPSHOT, filas con físico o reservado (equivale a
    /// includeEmpty=false, porque el reservado nunca supera el físico) y totales de todo el artículo.
    /// Se conservan el DTO, la ruta y el orden (código de ubicación y de lote).</summary>
    public async Task<ArticleLocations?> ArticleLocationsAsync(Guid articleId, int? page, int? pageSize,
        bool? includeInactiveLocations, CancellationToken cancellation)
    {
        var paging = Paging.From(page, pageSize);
        var buckets = await reader.ReadAsync(articleId, null, null, false, paging, cancellation,
            activeLocationsOnly: includeInactiveLocations == false);
        if (buckets is null) return null;
        return new ArticleLocations(buckets.AsOf, Ref(buckets.Article),
            new QuantityTotals(LocationText.Quantity(buckets.Totals.Physical), LocationText.Quantity(buckets.Totals.Reserved)),
            buckets.Items.Select(item => new ArticleLocationBucket(LocationText.Ref(item.Location), item.Lot.Code,
                LocationText.Quantity(item.Physical), LocationText.Quantity(item.Reserved))).ToList(),
            buckets.Page, buckets.PageSize, buckets.TotalCount);
    }

    /// <summary>US3: contenido de una ubicación por artículo y lote, desde StockBalances (SNAPSHOT).
    /// Sin total global: los artículos tienen unidades distintas.</summary>
    public Task<LocationStock?> StockAsync(Guid locationId, int? page, int? pageSize, CancellationToken cancellation)
    {
        var paging = Paging.From(page, pageSize);
        return database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
            var location = await database.Set<Location>().AsNoTracking().SingleOrDefaultAsync(value => value.Id == locationId, cancellation);
            if (location is null) return null;
            var asOf = DateTimeOffset.UtcNow;
            var rows = from balance in database.Set<StockBalance>().AsNoTracking()
                       join article in database.Set<Article>().AsNoTracking() on balance.ArticleId equals article.Id
                       join lot in database.Set<Lot>().AsNoTracking() on balance.LotId equals lot.Id
                       where balance.LocationId == locationId && (balance.Physical != 0m || balance.Reserved != 0m)
                       select new { balance.Physical, balance.Reserved, Article = article, LotCode = lot.Code };
            var hasReservations = await rows.AnyAsync(row => row.Reserved > 0m, cancellation);
            var count = await rows.CountAsync(cancellation);
            var items = await rows.OrderBy(row => row.Article.Sku).ThenBy(row => row.LotCode)
                .Skip(paging.Skip).Take(paging.PageSize).ToListAsync(cancellation);
            var inOpenCount = await usage.IsInOpenCountAsync(locationId, cancellation);
            await transaction.CommitAsync(cancellation);
            return new LocationStock(asOf, LocationText.Ref(location),
                items.Select(row => new LocationStockLine(Ref(row.Article), row.LotCode, LocationText.Quantity(row.Physical),
                    LocationText.Quantity(row.Reserved), row.Article.BaseUnit)).ToList(),
                hasReservations, inOpenCount, paging.Page, paging.PageSize, count);
        });
    }

    internal static ArticleRef Ref(Article article) => new(article.Id, article.Sku, article.Name, article.BaseUnit, article.IsActive);

    public static Task<int> ActiveReceivingCountAsync(InventoryDbContext database, CancellationToken cancellation) =>
        database.Set<Location>().AsNoTracking()
            .CountAsync(location => location.Kind == LocationKind.Receiving && location.IsActive, cancellation);
}
