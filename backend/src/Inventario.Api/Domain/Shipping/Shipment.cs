using Inventario.Api.Domain.Stock;

namespace Inventario.Api.Domain.Shipping;

public enum ShipmentState
{
    Draft,
    Confirmed
}

/// <summary>Línea pedida al guardar el borrador: línea de pedido, bucket explícito y cantidad.</summary>
public sealed record ShipmentLineRequest(Guid CustomerOrderLineId, Guid ArticleId, Guid LocationId, Guid LotId, decimal Quantity);

/// <summary>
/// Salida de mercancía contra un pedido de cliente (HU-03; data-model). Solo aplica reglas locales:
/// identidad, cantidades, una línea por línea de pedido y bucket, entre 1 y 100 líneas y la
/// transición única Draft → Confirmed. Pertenencia al pedido, lote del artículo, fracciones,
/// pendiente y existencias los decide el comando tras los bloqueos (research D-02, D-04). Una salida
/// confirmada es inmutable; además lo impiden los triggers de la base de datos (D-06).
/// </summary>
public sealed class Shipment
{
    public const int MaxLines = 100;

    private readonly List<ShipmentLine> lines = [];

    private Shipment() { }

    public Guid Id { get; private set; }
    public Guid CustomerOrderId { get; private set; }
    public ShipmentState State { get; private set; }
    /// <summary>Fecha y hora del hecho; editable en borrador. El límite futuro lo aplica el comando (D-07).</summary>
    public DateTimeOffset OccurredAt { get; private set; }
    /// <summary>Último registro: alta o edición del borrador y, al confirmar, la confirmación.</summary>
    public DateTimeOffset RecordedAt { get; private set; }
    public Guid ActorId { get; private set; }
    /// <summary>Operación que confirma; null en borrador. Los consumos la repiten (D-06, iteración).</summary>
    public Guid? OperationId { get; private set; }
    // rowversion: ETag de la salida (D-05).
    public byte[] Version { get; private set; } = [];
    public IReadOnlyList<ShipmentLine> Lines => lines;

    /// <summary>Borrador con identidad decidida por el comando (estable en los reintentos del ejecutor).</summary>
    public static Shipment CreateDraft(Guid id, Guid customerOrderId, DateTimeOffset occurredAt,
        IReadOnlyCollection<ShipmentLineRequest> lines, Guid actorId, DateTimeOffset recordedAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("La salida requiere un identificador.", nameof(id));
        if (customerOrderId == Guid.Empty) throw new ArgumentException("La salida requiere un pedido.", nameof(customerOrderId));
        var shipment = new Shipment { Id = id, CustomerOrderId = customerOrderId, State = ShipmentState.Draft };
        shipment.Apply(occurredAt, lines, actorId, recordedAt);
        return shipment;
    }

    /// <summary>Sustituye la fecha del hecho y todas las líneas de un borrador (PUT).</summary>
    public void ReplaceDraft(DateTimeOffset occurredAt, IReadOnlyCollection<ShipmentLineRequest> newLines,
        Guid actorId, DateTimeOffset recordedAt)
    {
        RequireDraft();
        Apply(occurredAt, newLines, actorId, recordedAt);
    }

    /// <summary>Draft → Confirmed con la operación que confirma; después no admite cambios.</summary>
    public void Confirm(Guid operationId, Guid actorId, DateTimeOffset recordedAt)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("La confirmación requiere su operación.", nameof(operationId));
        RequireActor(actorId);
        RequireDraft();
        State = ShipmentState.Confirmed;
        OperationId = operationId;
        ActorId = actorId;
        RecordedAt = recordedAt;
    }

    private void RequireDraft()
    {
        if (State != ShipmentState.Draft)
            throw new InvalidOperationException("La salida ya está confirmada y no admite cambios.");
    }

    private void Apply(DateTimeOffset occurredAt, IReadOnlyCollection<ShipmentLineRequest> requests, Guid actorId,
        DateTimeOffset recordedAt)
    {
        if (occurredAt == default) throw new ArgumentException("Indica la fecha y hora del hecho.", nameof(occurredAt));
        RequireActor(actorId);
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0) throw new ArgumentException("La salida necesita al menos una línea.", nameof(requests));
        if (requests.Count > MaxLines)
            throw new ArgumentException($"La salida admite como máximo {MaxLines} líneas.", nameof(requests));
        if (requests.GroupBy(value => (value.CustomerOrderLineId, value.LocationId, value.LotId)).Any(group => group.Count() > 1))
            throw new ArgumentException("Cada línea de pedido solo puede salir una vez de cada ubicación y lote.", nameof(requests));

        var created = requests.Select(request => new ShipmentLine(Id, request)).ToList();
        OccurredAt = occurredAt;
        ActorId = actorId;
        RecordedAt = recordedAt;
        lines.Clear();
        lines.AddRange(created);
    }

    private static void RequireActor(Guid actorId)
    {
        if (actorId == Guid.Empty) throw new ArgumentException("La salida requiere su responsable.", nameof(actorId));
    }
}

/// <summary>Cantidad servida de una línea de pedido desde un bucket. No cambia al confirmar (D-06).</summary>
public sealed class ShipmentLine
{
    private ShipmentLine() { }

    internal ShipmentLine(Guid shipmentId, ShipmentLineRequest request)
    {
        if (request.CustomerOrderLineId == Guid.Empty || request.ArticleId == Guid.Empty
            || request.LocationId == Guid.Empty || request.LotId == Guid.Empty)
            throw new ArgumentException("La línea requiere línea de pedido, artículo, ubicación y lote.", nameof(request));
        ShipmentId = shipmentId;
        CustomerOrderLineId = request.CustomerOrderLineId;
        ArticleId = request.ArticleId;
        LocationId = request.LocationId;
        LotId = request.LotId;
        Quantity = CustomerOrder.PositiveQuantity(request.Quantity, nameof(request.Quantity));
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ShipmentId { get; private set; }
    public Guid CustomerOrderLineId { get; private set; }
    public Guid ArticleId { get; private set; }
    public Guid LocationId { get; private set; }
    public Guid LotId { get; private set; }
    public decimal Quantity { get; private set; }
}

/// <summary>
/// Consumo de una asignación de reserva por una línea de salida (P-02, D-08). Append-only y ligado a
/// la operación que confirma la salida: un trigger rechaza insertarlo si la cabecera ya está
/// confirmada con otra operación (D-06, iteración del 2026-11-21).
/// </summary>
public sealed class ShipmentReservationConsumption
{
    private ShipmentReservationConsumption() { }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ShipmentLineId { get; private set; }
    public Guid ReservationAllocationId { get; private set; }
    public decimal Quantity { get; private set; }
    public Guid OperationId { get; private set; }

    public static ShipmentReservationConsumption Create(Guid shipmentLineId, Guid reservationAllocationId, decimal quantity,
        Guid operationId)
    {
        if (shipmentLineId == Guid.Empty || reservationAllocationId == Guid.Empty)
            throw new ArgumentException("El consumo requiere línea de salida y asignación.", nameof(shipmentLineId));
        if (operationId == Guid.Empty) throw new ArgumentException("El consumo requiere su operación.", nameof(operationId));
        return new ShipmentReservationConsumption
        {
            ShipmentLineId = shipmentLineId,
            ReservationAllocationId = reservationAllocationId,
            Quantity = CustomerOrder.PositiveQuantity(quantity, nameof(quantity)),
            OperationId = operationId
        };
    }
}
