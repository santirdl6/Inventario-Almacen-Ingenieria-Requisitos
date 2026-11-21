using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Stock.CustomerOrders;

/// <summary>
/// Lector en proceso del pedido para los escritores que lo consumen (HU-03 al servir; contrato
/// specs/004-consultar-existencias/contracts/hu03-integration.md § 2). Se llama con el
/// <see cref="OperationContext"/> del ejecutor, es decir, con el mismo DbContext y la misma
/// transacción, y **después** de bloquear <c>document:customer-order:{orderId}</c>.
/// No calcula otra reserva ni otro pendiente: solo publica los datos del pedido.
/// </summary>
public interface ICustomerOrderReader
{
    /// <summary>Después de bloquear document:customer-order:{orderId}. null si no existe.</summary>
    Task<CustomerOrderSnapshot?> ReadAsync(OperationContext context, Guid customerOrderId, CancellationToken cancellation);
}

public sealed record CustomerOrderSnapshot(Guid Id, string ExternalReference, string State, byte[] Version,
    IReadOnlyList<CustomerOrderLineSnapshot> Lines);

public sealed record CustomerOrderLineSnapshot(Guid Id, Guid ArticleId, decimal Ordered, decimal Shipped,
    IReadOnlyList<ReservationAllocationSnapshot> ActiveAllocations)
{
    public decimal Pending => Ordered - Shipped;
}

/// <summary>Asignación con remanente mayor que cero de una reserva Active; LotId y LotCode del bucket.</summary>
public sealed record ReservationAllocationSnapshot(Guid AllocationId, Guid ReservationId,
    Guid LocationId, Guid LotId, string LotCode, decimal Remaining);

/// <summary>
/// Implementación sin estado: lee en la transacción del comando (la del ejecutor) el estado
/// guardado del pedido, sin seguimiento, para que el escritor cargue después lo que vaya a cambiar.
/// No toma bloqueos: el llamador ya retiene document:customer-order:{orderId}.
/// </summary>
internal sealed class CustomerOrderReader : ICustomerOrderReader
{
    public async Task<CustomerOrderSnapshot?> ReadAsync(OperationContext context, Guid customerOrderId, CancellationToken cancellation)
    {
        var loaded = await CustomerOrderQueries.LoadAsync(context.Database, customerOrderId, cancellation);
        if (loaded is null) return null;
        var (order, reservations) = loaded.Value;
        var active = reservations.Where(reservation => reservation.State == ReservationState.Active)
            .SelectMany(reservation => reservation.Allocations)
            .Where(allocation => allocation.RemainingQuantity > 0).ToList();
        var lotIds = active.Select(allocation => allocation.LotId).Distinct().ToList();
        var lotCodes = await context.Database.Set<Lot>().AsNoTracking().Where(lot => lotIds.Contains(lot.Id))
            .ToDictionaryAsync(lot => lot.Id, lot => lot.Code, cancellation);
        var lineOf = reservations.ToDictionary(reservation => reservation.Id, reservation => reservation.CustomerOrderLineId);

        return new CustomerOrderSnapshot(order.Id, order.ExternalReference, order.State.ToString(), order.Version,
            order.Lines.OrderBy(line => line.Id).Select(line => new CustomerOrderLineSnapshot(line.Id, line.ArticleId,
                line.OrderedQuantity, line.ShippedQuantity,
                active.Where(allocation => lineOf[allocation.ReservationId] == line.Id).OrderBy(allocation => allocation.Id)
                    .Select(allocation => new ReservationAllocationSnapshot(allocation.Id, allocation.ReservationId,
                        allocation.LocationId, allocation.LotId, lotCodes[allocation.LotId], allocation.RemainingQuantity))
                    .ToList())).ToList());
    }
}
