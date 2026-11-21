namespace Inventario.Api.Domain.Receiving;

public enum ReceiptState { Draft, Confirmed }

public sealed class Receipt
{
    private readonly List<ReceiptLine> lines = [];
    private readonly List<ReceiptIncident> incidents = [];
    private Receipt() { }

    internal Receipt(Guid id, Guid purchaseOrderId, Guid actorId, DateTimeOffset createdAt)
    {
        Id = id;
        PurchaseOrderId = purchaseOrderId;
        CreatedBy = actorId;
        CreatedAt = createdAt.ToUniversalTime();
        OccurredAt = CreatedAt;
        Touch();
    }

    public Guid Id { get; private set; }
    public Guid PurchaseOrderId { get; private set; }
    public ReceiptState State { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string? SupplierDocumentReference { get; private set; }
    public string? SupportNotApplicableReason { get; private set; }
    public string? Reason { get; private set; }
    public string? CounterpartyReference { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RecordedAt { get; private set; }
    public Guid? ActorId { get; private set; }
    public Guid? OperationId { get; private set; }
    public Guid? MovementId { get; private set; }
    public Guid RevisionStamp { get; private set; }
    public byte[] Version { get; private set; } = [];
    public IReadOnlyList<ReceiptLine> Lines => lines;
    public IReadOnlyList<ReceiptIncident> Incidents => incidents;

    internal void Replace(DateTimeOffset occurredAt, string? support, string? absentSupportReason, string? reason,
        IEnumerable<ReceiptLine> newLines, IEnumerable<ReceiptIncident> newIncidents)
    {
        EnsureDraft();
        OccurredAt = occurredAt.ToUniversalTime();
        SupplierDocumentReference = support;
        SupportNotApplicableReason = absentSupportReason;
        Reason = reason;
        var replacementLines = newLines.ToArray();
        var replacementIncidents = newIncidents.ToArray();
        lines.Clear();
        lines.AddRange(replacementLines);
        incidents.Clear();
        incidents.AddRange(replacementIncidents);
        Touch();
    }

    internal void Confirm(Guid actor, Guid operation, Guid movement, DateTimeOffset recordedAt, string counterparty)
    {
        EnsureDraft();
        if (!lines.Any(line => line.AcceptedQuantity > 0) || lines.Any(line =>
                line.Allocations.Sum(allocation => allocation.Quantity) != line.AcceptedQuantity ||
                line.Allocations.Any(allocation => allocation.LotId is null)))
            throw new InvalidOperationException("La recepción no contiene una distribución confirmable.");
        if (string.IsNullOrWhiteSpace(Reason) ||
            (string.IsNullOrWhiteSpace(SupplierDocumentReference) == string.IsNullOrWhiteSpace(SupportNotApplicableReason)))
            throw new InvalidOperationException("La recepción no contiene motivo y soporte válidos.");
        State = ReceiptState.Confirmed;
        ActorId = actor;
        OperationId = operation;
        MovementId = movement;
        RecordedAt = recordedAt.ToUniversalTime();
        CounterpartyReference = counterparty;
        Touch();
    }

    internal void Touch() => RevisionStamp = Guid.NewGuid();

    private void EnsureDraft()
    {
        if (State != ReceiptState.Draft) throw new InvalidOperationException("La recepción ya está confirmada.");
    }
}
