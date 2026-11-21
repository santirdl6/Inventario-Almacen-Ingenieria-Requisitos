namespace Inventario.Api.Domain.Receiving;

public sealed class ReceiptIncident
{
    private ReceiptIncident() { }

    internal ReceiptIncident(Guid id, Guid receiptId)
    {
        Id = id;
        ReceiptId = receiptId;
    }

    public Guid Id { get; private set; }
    public Guid ReceiptId { get; private set; }
    public Guid? ReceiptLineId { get; private set; }
    public string Kind { get; private set; } = null!;
    public string ObservedDescription { get; private set; } = null!;
    public decimal? Quantity { get; private set; }
    public string? ObservedUnit { get; private set; }
    public string Reason { get; private set; } = null!;

    internal void Revise(Guid? lineId, string kind, string description, decimal? quantity, string? unit, string reason)
    {
        if (quantity is <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        ReceiptLineId = lineId;
        Kind = kind;
        ObservedDescription = description;
        Quantity = quantity;
        ObservedUnit = unit;
        Reason = reason;
    }
}
