using System.Data;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Shipping;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.MasterData.Queries;
using Inventario.Api.Features.Stock.CustomerOrders;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Shipping;

/// <summary>
/// Lecturas de la salida (HU-03; research D-12). El detalle lleva solo datos propios y su ETag; se
/// lee en una transacción SNAPSHOT, así que incluye solo hechos confirmados y un único instante. El
/// contexto con la revisión y la lista del pedido llegan en T012.
/// </summary>
public sealed class ShipmentQueries(InventoryDbContext database)
{
    /// <summary>GET /shipments/{id}: null si no existe.</summary>
    public Task<(ShipmentView View, byte[] Version)?> DetailAsync(Guid id, CancellationToken cancellation) =>
        database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
            var shipment = await database.Set<Shipment>().AsNoTracking().Include(value => value.Lines)
                .SingleOrDefaultAsync(value => value.Id == id, cancellation);
            if (shipment is null)
            {
                await transaction.CommitAsync(cancellation);
                return ((ShipmentView, byte[])?)null;
            }
            var consumptions = await ConsumptionsAsync(database, shipment, cancellation);
            await transaction.CommitAsync(cancellation);
            return (ShipmentView.From(shipment, consumptions), shipment.Version);
        });

    /// <summary>
    /// GET /shipments/{id}/context (T012, D-12): datos actuales del pedido, artículos, ubicaciones y
    /// lotes, sin ETag, con shipmentVersion y orderVersion. En borradores, la revisión por línea
    /// ordenada por Id. Todo en un único SNAPSHOT. null si la salida no existe.
    /// </summary>
    public Task<ShipmentContext?> ContextAsync(Guid id, CancellationToken cancellation) =>
        database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
            var shipment = await database.Set<Shipment>().AsNoTracking().Include(value => value.Lines)
                .SingleOrDefaultAsync(value => value.Id == id, cancellation);
            if (shipment is null)
            {
                await transaction.CommitAsync(cancellation);
                return null;
            }
            var asOf = DateTimeOffset.UtcNow;
            var order = await database.Set<CustomerOrder>().AsNoTracking().Include(value => value.Lines)
                .SingleAsync(value => value.Id == shipment.CustomerOrderId, cancellation);
            var lines = shipment.Lines.OrderBy(line => line.Id).ToList();
            var articleIds = lines.Select(line => line.ArticleId).Distinct().ToList();
            var locationIds = lines.Select(line => line.LocationId).Distinct().ToList();
            var lotIds = lines.Select(line => line.LotId).Distinct().ToList();

            var articles = await database.Set<Article>().AsNoTracking().Where(article => articleIds.Contains(article.Id))
                .OrderBy(article => article.Sku)
                .Select(article => new CustomerOrderContextArticle(article.Id, article.Sku, article.Name, article.BaseUnit,
                    article.IsActive, article.TracksLot, article.AllowsFractional))
                .ToListAsync(cancellation);
            var locations = await database.Set<Location>().AsNoTracking().Where(location => locationIds.Contains(location.Id))
                .OrderBy(location => location.Code)
                .Select(location => new CustomerOrderContextLocation(location.Id, location.Code, location.Aisle, location.Rack,
                    location.Level, location.IsActive))
                .ToListAsync(cancellation);
            var lots = await database.Set<Lot>().AsNoTracking().Where(lot => lotIds.Contains(lot.Id))
                .OrderBy(lot => lot.Code)
                .Select(lot => new CustomerOrderContextLot(lot.Id, lot.ArticleId, lot.Code, lot.IsUntracked))
                .ToListAsync(cancellation);
            var actor = await ActorNamesAsync(database, [shipment.ActorId], cancellation);
            var review = shipment.State == ShipmentState.Draft ? await ReviewAsync(database, order, lines, cancellation) : [];
            await transaction.CommitAsync(cancellation);

            return new ShipmentContext(EntityTags.Format(shipment.Version), EntityTags.Format(order.Version), asOf,
                actor[shipment.ActorId], new ShipmentContextOrder(order.Id, order.ExternalReference, order.CustomerReference,
                    order.State.ToString()), articles, locations, lots, review);
        });

    /// <summary>
    /// GET /shipments?customerOrderId= (T012, D-12; FR-018): salidas del pedido, borradores y
    /// confirmadas, por recordedAt descendente e Id. 422 sin pedido o con página fuera de rango; null si
    /// el pedido no existe.
    /// </summary>
    public Task<PageResult<ShipmentSummary>?> ListAsync(string? customerOrderId, int? page, int? pageSize,
        CancellationToken cancellation)
    {
        if (!Guid.TryParse(customerOrderId, out var orderId) || orderId == Guid.Empty)
            throw InventoryProblemException.Validation("customerOrderId", "Indica el pedido.");
        if (page is < 1 || pageSize is < 1 or > 100)
            throw InventoryProblemException.Validation(page is < 1 ? "page" : "pageSize",
                "La página empieza en 1 y el tamaño debe estar entre 1 y 100.");
        if (((long)(page ?? 1) - 1) * (pageSize ?? 25) > int.MaxValue)
            throw InventoryProblemException.Validation("page", "La página solicitada está fuera de rango.");
        var number = page ?? 1;
        var size = pageSize ?? 25;
        return database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
            if (!await database.Set<CustomerOrder>().AnyAsync(order => order.Id == orderId, cancellation))
            {
                await transaction.CommitAsync(cancellation);
                return (PageResult<ShipmentSummary>?)null;
            }
            var query = database.Set<Shipment>().AsNoTracking().Where(shipment => shipment.CustomerOrderId == orderId);
            var total = await query.CountAsync(cancellation);
            var rows = await query.OrderByDescending(shipment => shipment.RecordedAt).ThenBy(shipment => shipment.Id)
                .Skip((number - 1) * size).Take(size)
                .Select(shipment => new
                {
                    shipment.Id, shipment.State, shipment.OccurredAt, shipment.RecordedAt, shipment.ActorId,
                    LineCount = shipment.Lines.Count, Total = shipment.Lines.Sum(line => line.Quantity)
                })
                .ToListAsync(cancellation);
            var actors = await ActorNamesAsync(database, rows.Select(row => row.ActorId).Distinct().ToList(), cancellation);
            await transaction.CommitAsync(cancellation);
            return new PageResult<ShipmentSummary>(rows.Select(row => new ShipmentSummary(row.Id, row.State.ToString(),
                row.OccurredAt, row.RecordedAt, actors[row.ActorId], row.LineCount, ShipmentView.Quantity(row.Total))).ToList(),
                number, size, total);
        });
    }

    /// <summary>Cifras orientativas por línea (FR-006): el servidor las revalida al confirmar.</summary>
    private static async Task<IReadOnlyList<ShipmentReviewLine>> ReviewAsync(InventoryDbContext database, CustomerOrder order,
        IReadOnlyList<ShipmentLine> lines, CancellationToken cancellation)
    {
        var articleIds = lines.Select(line => line.ArticleId).Distinct().ToList();
        var locationIds = lines.Select(line => line.LocationId).Distinct().ToList();
        var balances = await database.Set<StockBalance>().AsNoTracking()
            .Where(balance => articleIds.Contains(balance.ArticleId) && locationIds.Contains(balance.LocationId))
            .ToListAsync(cancellation);
        var orderLineIds = lines.Select(line => line.CustomerOrderLineId).Distinct().ToList();
        var own = await database.Set<Reservation>().AsNoTracking()
            .Where(reservation => orderLineIds.Contains(reservation.CustomerOrderLineId) && reservation.State == ReservationState.Active)
            .SelectMany(reservation => reservation.Allocations.Select(allocation => new
            {
                reservation.CustomerOrderLineId, allocation.LocationId, allocation.LotId, allocation.RemainingQuantity
            }))
            .ToListAsync(cancellation);

        return lines.Select(line =>
        {
            var balance = balances.SingleOrDefault(value => value.ArticleId == line.ArticleId && value.LocationId == line.LocationId
                && value.LotId == line.LotId);
            var physical = balance?.Physical ?? 0m;
            var reserved = balance?.Reserved ?? 0m;
            var ownReserved = own.Where(value => value.CustomerOrderLineId == line.CustomerOrderLineId
                    && value.LocationId == line.LocationId && value.LotId == line.LotId)
                .Sum(value => value.RemainingQuantity);
            var pending = order.Lines.Single(value => value.Id == line.CustomerOrderLineId).PendingQuantity;
            var possible = Math.Max(0m, Math.Min(Math.Min(physical, ownReserved + (physical - reserved)), pending));
            return new ShipmentReviewLine(line.Id, ShipmentView.Quantity(pending), ShipmentView.Quantity(physical),
                ShipmentView.Quantity(reserved), ShipmentView.Quantity(ownReserved), ShipmentView.Quantity(reserved - ownReserved),
                ShipmentView.Quantity(possible));
        }).ToList();
    }

    /// <summary>Nombre de usuario de cada actor; si ya no existe, su identificador.</summary>
    private static async Task<Dictionary<Guid, string>> ActorNamesAsync(InventoryDbContext database, IReadOnlyList<Guid> ids,
        CancellationToken cancellation)
    {
        var names = await database.Users.AsNoTracking().Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.UserName ?? user.Id.ToString("D"), cancellation);
        return ids.ToDictionary(id => id, id => names.GetValueOrDefault(id) ?? id.ToString("D"));
    }

    /// <summary>Consumos de las líneas con la reserva de cada asignación; vacío en borradores.</summary>
    internal static async Task<IReadOnlyList<ShipmentConsumptionRow>> ConsumptionsAsync(InventoryDbContext database,
        Shipment shipment, CancellationToken cancellation)
    {
        if (shipment.State == ShipmentState.Draft) return [];
        var lineIds = shipment.Lines.Select(line => line.Id).ToList();
        return await (
                from consumption in database.Set<ShipmentReservationConsumption>().AsNoTracking()
                join allocation in database.Set<ReservationAllocation>().AsNoTracking()
                    on consumption.ReservationAllocationId equals allocation.Id
                where lineIds.Contains(consumption.ShipmentLineId)
                select new ShipmentConsumptionRow(consumption.Id, consumption.ShipmentLineId, consumption.ReservationAllocationId,
                    allocation.ReservationId, consumption.Quantity))
            .ToListAsync(cancellation);
    }
}
