namespace Inventario.Api.Domain.Receiving;

public sealed class ReceiptAllocation
{
    private ReceiptAllocation() { }

    internal ReceiptAllocation(Guid id, Guid lineId)
    {
        Id = id;
        ReceiptLineId = lineId;
    }

    public Guid Id { get; private set; }
    public Guid ReceiptLineId { get; private set; }
    public Guid LocationId { get; private set; }
    public string? LotCode { get; private set; }
    public Guid? LotId { get; private set; }
    public decimal Quantity { get; private set; }
    public string? LocationSnapshot { get; private set; }

    internal void Revise(Guid locationId, string? lotCode, decimal quantity)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        LocationId = locationId;
        LotCode = lotCode;
        Quantity = quantity;
    }

    internal void Freeze(Guid lotId, string effectiveLotCode, string locationSnapshot)
    {
        LotId = lotId;
        LotCode = effectiveLotCode;
        LocationSnapshot = locationSnapshot;
    }
}
