namespace Inventario.Api.Domain.Stock;

public enum MovementKind
{
    Receipt,
    Shipment,
    Adjustment,
    Reservation,
    Release,
    Correction
}

public enum StockEffect
{
    Receive,
    AdjustUp,
    ShipAvailable,
    AdjustDown,
    Reserve,
    Release,
    ShipReserved
}

// Cabecera del diario. Solo de adición: el rol SQL de ejecución no puede actualizarla ni borrarla.
public sealed class StockMovement
{
    public const int DocumentTypeMaxLength = 64;
    public const int ReasonMaxLength = 500;
    public const int ReferenceMaxLength = 200;

    private readonly List<StockEntry> entries = [];

    private StockMovement() { }

    internal StockMovement(MovementKind kind, string sourceDocumentType, Guid sourceDocumentId,
        string reason, Guid actorId, DateTimeOffset occurredAt, DateTimeOffset recordedAt,
        Guid operationId, string? supportReference, string? supportNotApplicableReason,
        string? counterpartyReference, string? counterpartyNotApplicableReason)
    {
        if (!HasValidCounterpartyEvidence(counterpartyReference, counterpartyNotApplicableReason))
            throw new ArgumentException("Indica la contraparte o el motivo por el que no aplica. Usa solo uno de los dos campos.");
        Kind = kind;
        SourceDocumentType = sourceDocumentType;
        SourceDocumentId = sourceDocumentId;
        Reason = reason;
        ActorId = actorId;
        OccurredAt = occurredAt;
        RecordedAt = recordedAt;
        OperationId = operationId;
        SupportReference = supportReference;
        SupportNotApplicableReason = supportNotApplicableReason;
        CounterpartyReference = counterpartyReference;
        CounterpartyNotApplicableReason = counterpartyNotApplicableReason;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public MovementKind Kind { get; private set; }
    public string SourceDocumentType { get; private set; } = "";
    public Guid SourceDocumentId { get; private set; }
    public Guid? OriginalMovementId { get; private set; }
    public string Reason { get; private set; } = "";
    public Guid ActorId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public Guid OperationId { get; private set; }
    public string? SupportReference { get; private set; }
    public string? SupportNotApplicableReason { get; private set; }
    public string? CounterpartyReference { get; private set; }
    public string? CounterpartyNotApplicableReason { get; private set; }
    public IReadOnlyList<StockEntry> Entries => entries;

    internal static bool HasValidCounterpartyEvidence(string? reference, string? reason) =>
        (reason is null && !string.IsNullOrWhiteSpace(reference))
        || (reference is null && !string.IsNullOrWhiteSpace(reason));

    internal void AddEntry(StockEntry entry) => entries.Add(entry);
    internal void LinkOriginal(Guid originalMovementId) => OriginalMovementId = originalMovementId;
}

public sealed class StockEntry
{
    private StockEntry() { }

    internal StockEntry(Guid movementId, Guid articleId, Guid locationId, Guid lotId,
        decimal quantity, string baseUnitSnapshot, StockEffect effect)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Una entrada del diario exige cantidad positiva.");
        MovementId = movementId;
        ArticleId = articleId;
        LocationId = locationId;
        LotId = lotId;
        Quantity = quantity;
        BaseUnitSnapshot = baseUnitSnapshot;
        Effect = effect;
        (PhysicalDelta, ReservedDelta) = effect switch
        {
            StockEffect.Receive or StockEffect.AdjustUp => (quantity, 0m),
            StockEffect.ShipAvailable or StockEffect.AdjustDown => (-quantity, 0m),
            StockEffect.Reserve => (0m, quantity),
            StockEffect.Release => (0m, -quantity),
            StockEffect.ShipReserved => (-quantity, -quantity),
            _ => throw new ArgumentOutOfRangeException(nameof(effect))
        };
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid MovementId { get; private set; }
    public Guid ArticleId { get; private set; }
    public Guid LocationId { get; private set; }
    public Guid LotId { get; private set; }
    public decimal Quantity { get; private set; }
    public string BaseUnitSnapshot { get; private set; } = "";
    public StockEffect Effect { get; private set; }
    public decimal PhysicalDelta { get; private set; }
    public decimal ReservedDelta { get; private set; }
}
