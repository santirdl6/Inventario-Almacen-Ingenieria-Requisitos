using System.Data;
using Inventario.Api.Domain.Shipping;
using Inventario.Api.Domain.Stock;
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
