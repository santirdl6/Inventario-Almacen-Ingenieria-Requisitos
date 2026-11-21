namespace Inventario.Api.Domain.Purchasing;

public enum PurchaseAuditAction { Create, Update, RequestSend, RetrySend }

public sealed class PurchaseOrderAudit
{
    private PurchaseOrderAudit() { }
    internal PurchaseOrderAudit(Guid orderId, PurchaseAuditAction action, Guid actor, Guid operation,
        string? before, string? after)
    {
        Id = Guid.NewGuid(); PurchaseOrderId = orderId; Action = action; ActorId = actor;
        OperationId = operation; Before = before; After = after; RecordedAt = DateTimeOffset.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid PurchaseOrderId { get; private set; }
    public PurchaseAuditAction Action { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid OperationId { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public string? Before { get; private set; }
    public string? After { get; private set; }
}
