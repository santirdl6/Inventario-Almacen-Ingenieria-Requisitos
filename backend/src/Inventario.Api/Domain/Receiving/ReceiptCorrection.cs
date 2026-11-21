namespace Inventario.Api.Domain.Receiving;

public sealed class ReceiptCorrection
{
    private ReceiptCorrection() { }
    internal ReceiptCorrection(Guid id, Guid receiptId, Guid originalMovementId, Guid movementId, Guid actorId,
        Guid operationId, DateTimeOffset recordedAt, string reason, string? reference)
    { Id = id; ReceiptId = receiptId; OriginalMovementId = originalMovementId; MovementId = movementId; ActorId = actorId;
        OperationId = operationId; RecordedAt = recordedAt; Reason = reason; Reference = reference; }
    public Guid Id { get; private set; }
    public Guid ReceiptId { get; private set; }
    public Guid OriginalMovementId { get; private set; }
    public Guid MovementId { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid OperationId { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public string Reason { get; private set; } = "";
    public string? Reference { get; private set; }
}
