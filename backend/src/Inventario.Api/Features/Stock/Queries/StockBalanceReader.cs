using System.Data;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.Locations;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Stock.Queries;

/// <summary>
/// Agregado único de existencias por artículo (HU-05 T006; research D-02, D-03). Es la única
/// proyección de StockBalances ⋈ Locations ⋈ Lots: GET /stock la usa ahora y, desde T020,
/// también GET /articles/{id}/locations de HU-02. No hay otro contador ni otro cálculo: el
/// disponible es físico − reservado, calculado después de sumar (FR-003).
/// <para>Página, totales y recuento se leen en una sola transacción SNAPSHOT, así que filas y
/// totales corresponden al mismo instante (FR-006) y solo incluyen hechos confirmados (FR-005).</para>
/// </summary>
public sealed class StockBalanceReader(InventoryDbContext database)
{
    /// <summary>
    /// Devuelve null si el artículo no existe. Con <paramref name="includeEmpty"/> en false solo
    /// hay filas con físico mayor que cero; los totales son siempre los del filtro completo, porque
    /// las filas a cero no aportan nada (el reservado nunca supera el físico).
    /// </summary>
    public Task<StockBuckets?> ReadAsync(Guid articleId, Guid? locationId, Guid? lotId, bool includeEmpty, Paging paging,
        CancellationToken cancellation) =>
        database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
            var article = await database.Set<Article>().AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == articleId, cancellation);
            if (article is null) return (StockBuckets?)null;
            var asOf = DateTimeOffset.UtcNow;

            var filtered = database.Set<StockBalance>().AsNoTracking().Where(balance => balance.ArticleId == articleId);
            if (locationId is { } location) filtered = filtered.Where(balance => balance.LocationId == location);
            if (lotId is { } lot) filtered = filtered.Where(balance => balance.LotId == lot);

            var totals = await filtered.GroupBy(_ => 1)
                .Select(group => new { Physical = group.Sum(value => value.Physical), Reserved = group.Sum(value => value.Reserved) })
                .SingleOrDefaultAsync(cancellation);

            var visible = includeEmpty ? filtered : filtered.Where(balance => balance.Physical > 0m);
            var rows = from balance in visible
                       join place in database.Set<Location>().AsNoTracking() on balance.LocationId equals place.Id
                       join bucket in database.Set<Lot>().AsNoTracking() on balance.LotId equals bucket.Id
                       select new { balance.Physical, balance.Reserved, Location = place, Lot = bucket };
            var count = await rows.CountAsync(cancellation);
            // Orden estable del contrato: código de ubicación, código de lote e Id de ubicación.
            var items = await rows.OrderBy(row => row.Location.Code).ThenBy(row => row.Lot.Code).ThenBy(row => row.Location.Id)
                .Skip(paging.Skip).Take(paging.PageSize).ToListAsync(cancellation);
            await transaction.CommitAsync(cancellation);

            return new StockBuckets(asOf, article,
                new StockBucketTotals(totals?.Physical ?? 0m, totals?.Reserved ?? 0m),
                items.Select(row => new StockBucket(row.Location, row.Lot, row.Physical, row.Reserved)).ToList(),
                paging.Page, paging.PageSize, count);
        });
}

public sealed record StockBucketTotals(decimal Physical, decimal Reserved)
{
    public decimal Available => Physical - Reserved;
}

public sealed record StockBucket(Location Location, Lot Lot, decimal Physical, decimal Reserved)
{
    public decimal Available => Physical - Reserved;
}

public sealed record StockBuckets(DateTimeOffset AsOf, Article Article, StockBucketTotals Totals,
    IReadOnlyList<StockBucket> Items, int Page, int PageSize, int TotalCount);
