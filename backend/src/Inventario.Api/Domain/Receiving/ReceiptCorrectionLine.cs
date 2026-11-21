namespace Inventario.Api.Domain.Receiving;

public sealed class ReceiptCorrectionLine
{
    private ReceiptCorrectionLine() { }
    internal ReceiptCorrectionLine(Guid correctionId, Guid allocationId, decimal quantity)
    { Id = Guid.NewGuid(); CorrectionId = correctionId; ReceiptAllocationId = allocationId; Quantity = quantity; }
    public Guid Id { get; private set; }
    public Guid CorrectionId { get; private set; }
    public Guid ReceiptAllocationId { get; private set; }
    public decimal Quantity { get; private set; }
}
