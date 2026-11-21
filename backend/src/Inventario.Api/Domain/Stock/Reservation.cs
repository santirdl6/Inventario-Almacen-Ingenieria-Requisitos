namespace Inventario.Api.Domain.Stock;

public enum ReservationState
{
    Active,
    Consumed,
    Released
}

/// <summary>Asignación pedida al reservar: bucket explícito artículo/ubicación/lote y cantidad.</summary>
public sealed record ReservationAllocationRequest(Guid ArticleId, Guid LocationId, Guid LotId, decimal Quantity);

/// <summary>
/// Reserva de una línea de pedido de cliente (HU-05). Solo aplica reglas locales: cantidades,
/// remanente ≥ 0 y estado derivado. El disponible del bucket, el pendiente de la línea y las
/// reservas ajenas los comprueba el comando tras los bloqueos, con StockCore (research D-09).
/// </summary>
public sealed class Reservation
{
    private readonly List<ReservationAllocation> allocations = [];

    private Reservation() { }

    public Guid Id { get; private set; }
    public Guid CustomerOrderLineId { get; private set; }
    public ReservationState State { get; private set; }
    // rowversion: ETag de la reserva; cada liberación (y el consumo de HU-03) lo renueva (D-07).
    public byte[] Version { get; private set; } = [];
    public IReadOnlyList<ReservationAllocation> Allocations => allocations;

    /// <summary>Reserva nueva con identidad del servidor; también es el documento origen del movimiento.</summary>
    public static Reservation Create(Guid id, Guid customerOrderLineId,
        IReadOnlyCollection<ReservationAllocationRequest> allocations)
    {
        if (id == Guid.Empty) throw new ArgumentException("La reserva requiere un identificador.", nameof(id));
        if (customerOrderLineId == Guid.Empty)
            throw new ArgumentException("La reserva requiere una línea de pedido.", nameof(customerOrderLineId));
        ArgumentNullException.ThrowIfNull(allocations);
        if (allocations.Count == 0)
            throw new ArgumentException("La reserva necesita al menos una asignación.", nameof(allocations));
        // P-02: una asignación por bucket y reserva; dos en el mismo bucket deben unirse.
        if (allocations.GroupBy(value => (value.ArticleId, value.LocationId, value.LotId)).Any(group => group.Count() > 1))
            throw new ArgumentException("Cada ubicación y lote solo puede aparecer en una asignación.", nameof(allocations));

        var reservation = new Reservation { Id = id, CustomerOrderLineId = customerOrderLineId, State = ReservationState.Active };
        foreach (var allocation in allocations)
            reservation.allocations.Add(new ReservationAllocation(id, allocation));
        return reservation;
    }

    /// <summary>
    /// Estado derivado del modelo común: Active si queda remanente; si no, Consumed cuando todo lo
    /// asignado se consumió y Released en otro caso. Cada escritor lo recalcula.
    /// </summary>
    public void DeriveState() =>
        State = allocations.Any(value => value.RemainingQuantity > 0) ? ReservationState.Active
            : allocations.All(value => value.ConsumedQuantity == value.AllocatedQuantity) ? ReservationState.Consumed
            : ReservationState.Released;
}

/// <summary>Cantidad comprometida en un bucket; Remaining = Allocated − Consumed − Released ≥ 0.</summary>
public sealed class ReservationAllocation
{
    private ReservationAllocation() { }

    internal ReservationAllocation(Guid reservationId, ReservationAllocationRequest request)
    {
        if (request.ArticleId == Guid.Empty || request.LocationId == Guid.Empty || request.LotId == Guid.Empty)
            throw new ArgumentException("La asignación requiere artículo, ubicación y lote.", nameof(request));
        ReservationId = reservationId;
        ArticleId = request.ArticleId;
        LocationId = request.LocationId;
        LotId = request.LotId;
        AllocatedQuantity = CustomerOrder.PositiveQuantity(request.Quantity, nameof(request.Quantity));
        RemainingQuantity = AllocatedQuantity;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ReservationId { get; private set; }
    public Guid ArticleId { get; private set; }
    public Guid LocationId { get; private set; }
    public Guid LotId { get; private set; }
    public decimal AllocatedQuantity { get; private set; }
    public decimal ConsumedQuantity { get; private set; }
    public decimal ReleasedQuantity { get; private set; }
    // En SQL será una columna calculada persistida (T004); aquí se mantiene igual para que las
    // reglas locales la usen antes de guardar. EF no envía su valor al ser calculada.
    public decimal RemainingQuantity { get; private set; }

    /// <summary>Libera parte del remanente: ReleasedQuantity solo crece y nunca supera lo comprometido.</summary>
    public void Release(decimal quantity)
    {
        CustomerOrder.PositiveQuantity(quantity, nameof(quantity));
        if (quantity > RemainingQuantity)
            throw new InvalidOperationException("No se puede liberar más que el remanente de la asignación.");
        ReleasedQuantity += quantity;
        RemainingQuantity = AllocatedQuantity - ConsumedQuantity - ReleasedQuantity;
    }
}
