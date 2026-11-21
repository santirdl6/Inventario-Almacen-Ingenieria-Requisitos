namespace Inventario.Api.Domain.Receiving;

public sealed class ReceiptLine
{
    private readonly List<ReceiptAllocation> allocations = [];
    private ReceiptLine() { }

    internal ReceiptLine(Guid id, Guid receiptId, Guid purchaseOrderLineId)
    {
        Id = id;
        ReceiptId = receiptId;
        PurchaseOrderLineId = purchaseOrderLineId;
    }

    public Guid Id { get; private set; }
    public Guid ReceiptId { get; private set; }
    public Guid PurchaseOrderLineId { get; private set; }
    public decimal ReportedQuantity { get; private set; }
    public decimal AcceptedQuantity { get; private set; }
    public string? DiscrepancyReason { get; private set; }
    public decimal? OrderedAtConfirmation { get; private set; }
    public decimal? ReceivedBeforeConfirmation { get; private set; }
    public decimal? PendingAfterConfirmation { get; private set; }
    public string? ArticleSnapshot { get; private set; }
    public IReadOnlyList<ReceiptAllocation> Allocations => allocations;

    internal void Revise(decimal reported, decimal accepted, string? discrepancyReason, IEnumerable<ReceiptAllocation> replacements)
    {
        if (reported < 0 || accepted < 0 || accepted > reported ||
            (reported > accepted && string.IsNullOrWhiteSpace(discrepancyReason)))
            throw new ArgumentException("La diferencia entre lo reportado y lo aceptado requiere un motivo.");
        ReportedQuantity = reported;
        AcceptedQuantity = accepted;
        DiscrepancyReason = discrepancyReason;
        var values = replacements.ToArray();
        allocations.Clear();
        allocations.AddRange(values);
    }

    internal void Freeze(decimal ordered, decimal receivedBefore, string articleSnapshot)
    {
        OrderedAtConfirmation = ordered;
        ReceivedBeforeConfirmation = receivedBefore;
        PendingAfterConfirmation = ordered - receivedBefore - AcceptedQuantity;
        ArticleSnapshot = articleSnapshot;
    }
}
