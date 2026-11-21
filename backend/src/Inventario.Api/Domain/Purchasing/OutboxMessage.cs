namespace Inventario.Api.Domain.Purchasing;

public enum PurchaseOutboxState { Pending, Processing, Sent, Failed }

public sealed class OutboxMessage
{
    private OutboxMessage() { }
    public Guid Id { get; private set; }
    public string Type { get; private set; } = "PurchaseOrderEmail";
    public Guid DocumentId { get; private set; }
    public Guid DeliveryId { get; private set; }
    public PurchaseOutboxState State { get; private set; }
    public Guid ActiveAuthorizationOperationId { get; private set; }
    public Guid AuthorizedBy { get; private set; }
    public int Cycle { get; private set; } = 1;
    public int CycleAttempts { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public Guid? LeaseOwner { get; private set; }
    public long LeaseGeneration { get; private set; }
    public DateTimeOffset? LeaseUntil { get; private set; }
    public Guid? CurrentAttemptId { get; private set; }
    public Guid? FinishOperationId { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? LastErrorSummary { get; private set; }
    public bool AcceptanceUncertain { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public byte[] Version { get; private set; } = [];

    internal static OutboxMessage Create(PurchaseOrderDelivery delivery, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), DocumentId = delivery.PurchaseOrderId, DeliveryId = delivery.Id,
        ActiveAuthorizationOperationId = delivery.RequestOperationId, AuthorizedBy = delivery.RequestedBy,
        CreatedAt = now, NextAttemptAt = now
    };

    internal bool CanClaim(DateTimeOffset now) => State == PurchaseOutboxState.Pending && NextAttemptAt <= now
        || State == PurchaseOutboxState.Processing && LeaseUntil <= now;

    internal bool Owns(Guid owner, Guid attempt, long generation, DateTimeOffset now) =>
        State == PurchaseOutboxState.Processing && LeaseOwner == owner && CurrentAttemptId == attempt
        && LeaseGeneration == generation && LeaseUntil > now;

    internal bool Claim(Guid owner, Guid attempt, Guid finishOperation, DateTimeOffset now, int leaseSeconds, int maximum)
    {
        if (owner == Guid.Empty || attempt == Guid.Empty || finishOperation == Guid.Empty || maximum is < 1 or > 5
            || leaseSeconds <= 0) throw new ArgumentException("La concesión requiere identidades y límites válidos.");
        if (!CanClaim(now)) return false;
        if (State == PurchaseOutboxState.Processing) AcceptanceUncertain = true;
        if (CycleAttempts >= maximum)
        {
            LastErrorCode = "AttemptBudgetExhausted";
            LastErrorSummary = "Se agotaron los intentos automáticos. Comprueba la incidencia antes de reintentar el mismo envío.";
            State = PurchaseOutboxState.Failed; ClearLease(); return false;
        }
        State = PurchaseOutboxState.Processing; LeaseGeneration++;
        Attempts++; CycleAttempts++; LeaseOwner = owner; CurrentAttemptId = attempt;
        FinishOperationId = finishOperation; LeaseUntil = now.AddSeconds(leaseSeconds);
        return true;
    }

    internal void Finish(bool accepted, bool permanent, bool uncertain, string? code, string? summary,
        DateTimeOffset now, int maximum)
    {
        if (State != PurchaseOutboxState.Processing) throw new InvalidOperationException("No hay concesión que finalizar.");
        if (code?.Length > 64 || summary?.Length > 500) throw new ArgumentException("La incidencia pública excede el límite.");
        AcceptanceUncertain |= uncertain;
        LastErrorCode = accepted ? null : code; LastErrorSummary = accepted ? null : summary;
        if (accepted) { State = PurchaseOutboxState.Sent; SentAt = now; }
        else if (permanent || CycleAttempts >= maximum) State = PurchaseOutboxState.Failed;
        else { State = PurchaseOutboxState.Pending; NextAttemptAt = now.AddSeconds(new[] { 5, 30, 120, 300 }[CycleAttempts - 1]); }
        ClearLease();
    }

    internal void Retry(Guid actor, Guid operation, DateTimeOffset now)
    {
        if (State != PurchaseOutboxState.Failed || actor == Guid.Empty || operation == Guid.Empty)
            throw new InvalidOperationException("Solo un fallo conocido admite otra autorización.");
        State = PurchaseOutboxState.Pending; Cycle++; CycleAttempts = 0;
        ActiveAuthorizationOperationId = operation; AuthorizedBy = actor; NextAttemptAt = now;
    }

    private void ClearLease() { LeaseOwner = null; LeaseUntil = null; CurrentAttemptId = null; FinishOperationId = null; }
}
