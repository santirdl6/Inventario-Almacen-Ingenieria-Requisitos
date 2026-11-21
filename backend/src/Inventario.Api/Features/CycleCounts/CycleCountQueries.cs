using System.Data;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.Shipping;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.MasterData.Queries;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.CycleCounts;

/// <summary>
/// Lecturas de conteos (HU-04; research D-12). Cada una en una transacción SNAPSHOT: un único instante
/// y solo hechos confirmados. El detalle lleva solo datos propios y su ETag; el contexto, datos actuales
/// de otros agregados sin ETag y con countVersion.
/// </summary>
public sealed class CycleCountQueries(InventoryDbContext database)
{
    /// <summary>GET /cycle-counts/{id}: null si no existe.</summary>
    public Task<(CycleCountView View, byte[] Version)?> DetailAsync(Guid id, CancellationToken cancellation) =>
        SnapshotAsync(async () =>
        {
            var count = await LoadAsync(id, cancellation);
            return count is null ? ((CycleCountView, byte[])?)null : (CycleCountView.From(count), count.Version);
        }, cancellation);

    /// <summary>GET /cycle-counts/{id}/context: null si no existe.</summary>
    public Task<CycleCountContext?> ContextAsync(Guid id, CancellationToken cancellation) =>
        SnapshotAsync(async () =>
        {
            var count = await LoadAsync(id, cancellation);
            if (count is null) return null;
            var lines = count.Lines;
            var articleIds = lines.Select(line => line.ArticleId).Distinct().ToList();
            var locationIds = count.Locations.Select(location => location.LocationId).ToList();
            var articles = await database.Articles.AsNoTracking().Where(article => articleIds.Contains(article.Id))
                .OrderBy(article => article.Id)
                .Select(article => new CountArticleContext(article.Id, article.Sku, article.Name, article.BaseUnit,
                    article.AllowsFractional, article.TracksLot, article.IsActive))
                .ToListAsync(cancellation);
            var locations = await database.Set<Location>().AsNoTracking().Where(location => locationIds.Contains(location.Id))
                .OrderBy(location => location.Id)
                .Select(location => new CountLocationContext(location.Id, location.Code, location.IsActive))
                .ToListAsync(cancellation);
            // Reservado actual de cada bucket de las líneas (FR-011: la revisión lo compara con el contado).
            var reserved = await (
                    from balance in database.Set<StockBalance>().AsNoTracking()
                    join lot in database.Set<Lot>().AsNoTracking() on balance.LotId equals lot.Id
                    where articleIds.Contains(balance.ArticleId) && locationIds.Contains(balance.LocationId)
                    select new { balance.ArticleId, balance.LocationId, LotCode = lot.Code, balance.Reserved })
                .ToListAsync(cancellation);
            var buckets = lines.Select(line => (line.ArticleId, line.LocationId, line.LotCode)).ToHashSet();
            var shipmentIds = lines.Where(line => line.CorrectsShipmentId is not null)
                .Select(line => line.CorrectsShipmentId!.Value).Distinct().ToList();
            var shipments = await (
                    from shipment in database.Set<Shipment>().AsNoTracking()
                    join order in database.Set<CustomerOrder>().AsNoTracking() on shipment.CustomerOrderId equals order.Id
                    where shipmentIds.Contains(shipment.Id)
                    orderby shipment.Id
                    select new CountShipmentContext(shipment.Id, order.Id, order.ExternalReference))
                .ToListAsync(cancellation);
            var userIds = new[] { count.CreatedBy, count.StartedBy, count.SubmittedBy, count.ApprovedBy, count.CancelledBy }
                .Concat(lines.Select(line => line.CountedBy)).Concat(lines.Select(line => line.ReasonBy))
                .OfType<Guid>().Distinct().ToList();
            var users = await database.Users.AsNoTracking().Where(user => userIds.Contains(user.Id)).OrderBy(user => user.Id)
                .Select(user => new CountUserContext(user.Id, user.UserName ?? ""))
                .ToListAsync(cancellation);
            return new CycleCountContext(EntityTags.Format(count.Version), articles, locations,
                reserved.Where(row => buckets.Contains((row.ArticleId, row.LocationId, row.LotCode)))
                    .OrderBy(row => row.ArticleId).ThenBy(row => row.LocationId).ThenBy(row => row.LotCode, StringComparer.Ordinal)
                    .Select(row => new CountBucketReserved(row.ArticleId, row.LocationId, row.LotCode, CycleCountView.Quantity(row.Reserved)))
                    .ToList(),
                shipments, users.Select(user => user with { Name = user.Name.Length > 0 ? user.Name : user.Id.ToString("D") }).ToList());
        }, cancellation);

    /// <summary>GET /cycle-counts?state=&amp;page=&amp;pageSize=: createdAt descendente, luego id.</summary>
    public Task<PageResult<CycleCountSummary>> ListAsync(string? state, int? page, int? pageSize, CancellationToken cancellation)
    {
        CycleCountState? filter = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            if (!Enum.TryParse<CycleCountState>(state.Trim(), ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed))
                throw InventoryProblemException.Validation("state",
                    "El estado debe ser Draft, Counting, AwaitingApproval, Confirmed o Cancelled.");
            filter = parsed;
        }
        var paging = Locations.Paging.From(page, pageSize);
        return SnapshotAsync(async () =>
        {
            var query = database.Set<CycleCount>().AsNoTracking();
            if (filter is { } value) query = query.Where(count => count.State == value);
            var total = await query.CountAsync(cancellation);
            var rows = await query.OrderByDescending(count => count.CreatedAt).ThenBy(count => count.Id)
                .Skip(paging.Skip).Take(paging.PageSize)
                .Select(count => new
                {
                    count.Id, count.CountCode, count.State, count.CreatedBy, count.CreatedAt, count.StartedAt,
                    ClosedAt = count.ApprovedAt ?? count.CancelledAt,
                    Locations = count.Locations.OrderBy(location => location.LocationId).Select(location => location.LocationId).ToList()
                })
                .ToListAsync(cancellation);
            return new PageResult<CycleCountSummary>(rows.Select(row => new CycleCountSummary(row.Id, row.CountCode, row.State.ToString(),
                row.Locations, row.CreatedBy, row.CreatedAt, row.StartedAt, row.ClosedAt)).ToList(), paging.Page, paging.PageSize, total);
        }, cancellation);
    }

    /// <summary>
    /// GET /cycle-counts/{id}/lines/{lineId}/shipment-candidates (T012, D-09): salidas confirmadas con alguna
    /// línea en el bucket de la línea, de la más reciente a la más antigua, con su pedido, la cantidad en el
    /// bucket y las líneas de conteo ya vinculadas. null si el conteo o la línea no existen.
    /// </summary>
    public Task<PageResult<ShipmentCandidate>?> ShipmentCandidatesAsync(Guid id, Guid lineId, int? page, int? pageSize,
        CancellationToken cancellation)
    {
        var paging = Locations.Paging.From(page, pageSize);
        return SnapshotAsync(async () =>
        {
            var line = await database.Set<CountLine>().AsNoTracking().SingleOrDefaultAsync(value => value.Id == lineId && value.CountId == id,
                cancellation);
            if (line is null) return (PageResult<ShipmentCandidate>?)null;
            // Un hallazgo de lote nuevo no tiene lote: ninguna salida pudo tocar su bucket.
            if (line.LotId is not { } lotId) return new PageResult<ShipmentCandidate>([], paging.Page, paging.PageSize, 0);
            var query =
                from shipment in database.Set<Shipment>().AsNoTracking()
                join order in database.Set<CustomerOrder>().AsNoTracking() on shipment.CustomerOrderId equals order.Id
                where shipment.State == ShipmentState.Confirmed && shipment.Lines.Any(value =>
                    value.ArticleId == line.ArticleId && value.LocationId == line.LocationId && value.LotId == lotId)
                select new { shipment, order.ExternalReference };
            var total = await query.CountAsync(cancellation);
            var rows = await query.OrderByDescending(row => row.shipment.RecordedAt).ThenBy(row => row.shipment.Id)
                .Skip(paging.Skip).Take(paging.PageSize)
                .Select(row => new
                {
                    row.shipment.Id, row.shipment.CustomerOrderId, row.ExternalReference, row.shipment.RecordedAt,
                    Quantity = row.shipment.Lines.Where(value => value.ArticleId == line.ArticleId && value.LocationId == line.LocationId
                        && value.LotId == lotId).Sum(value => value.Quantity)
                })
                .ToListAsync(cancellation);
            var ids = rows.Select(row => row.Id).ToList();
            var links = await database.Set<CountLine>().AsNoTracking()
                .Where(value => value.CorrectsShipmentId != null && ids.Contains(value.CorrectsShipmentId.Value))
                .Select(value => new { value.Id, Shipment = value.CorrectsShipmentId!.Value }).ToListAsync(cancellation);
            return new PageResult<ShipmentCandidate>(rows.Select(row => new ShipmentCandidate(row.Id, row.CustomerOrderId, row.ExternalReference,
                row.RecordedAt, CycleCountView.Quantity(row.Quantity),
                links.Where(link => link.Shipment == row.Id).Select(link => link.Id).OrderBy(value => value).ToList())).ToList(),
                paging.Page, paging.PageSize, total);
        }, cancellation);
    }

    private Task<CycleCount?> LoadAsync(Guid id, CancellationToken cancellation) =>
        database.Set<CycleCount>().AsNoTracking().Include(value => value.Locations).Include(value => value.Lines)
            .AsSplitQuery().SingleOrDefaultAsync(value => value.Id == id, cancellation);

    private Task<T> SnapshotAsync<T>(Func<Task<T>> read, CancellationToken cancellation) =>
        database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
            var result = await read();
            await transaction.CommitAsync(cancellation);
            return result;
        });
}
