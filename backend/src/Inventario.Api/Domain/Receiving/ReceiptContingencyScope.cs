namespace Inventario.Api.Domain.Receiving;

public enum ReceiptContingencyScopeState { Suspended, ReopeningAuthorized }

// A scope is not a stock freeze by itself. It records the reconciliation evidence
// and cannot declare reopening while a voucher or any difference remains unresolved.
public sealed class ReceiptContingencyScope
{
    private ReceiptContingencyScope() { }
    internal ReceiptContingencyScope(Guid id, string purchaseOrders, string locations, string expectedVouchers, Guid createdBy)
    {
        Id = id; PurchaseOrders = purchaseOrders; Locations = locations; ExpectedVouchers = expectedVouchers;
        CreatedBy = createdBy; CreatedAt = DateTimeOffset.UtcNow; Touch();
    }
    public Guid Id { get; private set; }
    public ReceiptContingencyScopeState State { get; private set; }
    public string PurchaseOrders { get; private set; } = "[]";
    public string Locations { get; private set; } = "[]";
    public string ExpectedVouchers { get; private set; } = "[]";
    public string Differences { get; private set; } = "[]";
    public string? JournalEvidence { get; private set; }
    public string? StockEvidence { get; private set; }
    public string? PurchaseEvidence { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? AuthorizedBy { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public Guid? AuthorizationOperationId { get; private set; }
    public Guid RevisionStamp { get; private set; }
    public byte[] Version { get; private set; } = [];
    internal void Revise(string purchases, string locations, string vouchers, string differences, string? journal, string? stock, string? purchase)
    {
        EnsureSuspended(); PurchaseOrders = purchases; Locations = locations; ExpectedVouchers = vouchers; Differences = differences;
        JournalEvidence = journal; StockEvidence = stock; PurchaseEvidence = purchase; Touch();
    }
    internal void Authorize(Guid actor, Guid operation, DateTimeOffset instant)
    { EnsureSuspended(); State = ReceiptContingencyScopeState.ReopeningAuthorized; AuthorizedBy = actor; AuthorizationOperationId = operation; AuthorizedAt = instant; Touch(); }
    private void EnsureSuspended() { if (State != ReceiptContingencyScopeState.Suspended) throw new InvalidOperationException("El alcance ya tiene reapertura autorizada."); }
    private void Touch() => RevisionStamp = Guid.NewGuid();
}
