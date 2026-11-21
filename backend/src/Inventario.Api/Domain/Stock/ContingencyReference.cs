namespace Inventario.Api.Domain.Stock;

public sealed class ContingencyReference
{
    private ContingencyReference() { }
    internal ContingencyReference(string voucherId, DateTimeOffset occurredAt, string responsibleReference,
        string normalizedData, Guid receiptId, Guid operationId, Guid importedBy, DateTimeOffset reconciledAt)
    {
        VoucherId = voucherId; OccurredAt = occurredAt; ResponsibleReference = responsibleReference;
        NormalizedData = normalizedData; ReceiptId = receiptId; ReconciledOperationId = operationId;
        ImportedBy = importedBy; ReconciledAt = reconciledAt;
    }
    public string VoucherId { get; private set; } = "";
    public string Type { get; private set; } = "Receive";
    public DateTimeOffset OccurredAt { get; private set; }
    public string ResponsibleReference { get; private set; } = "";
    public string NormalizedData { get; private set; } = "";
    public Guid ReceiptId { get; private set; }
    public Guid ReconciledOperationId { get; private set; }
    public Guid ImportedBy { get; private set; }
    public DateTimeOffset ReconciledAt { get; private set; }
}
