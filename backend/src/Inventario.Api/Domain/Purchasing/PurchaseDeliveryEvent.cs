namespace Inventario.Api.Domain.Purchasing;

public enum PurchaseDeliveryEventKind
{
    Requested, Retried, Claimed, LeaseExpired, Accepted, TransientFailure, PermanentFailure, Uncertain, LateObservation
}
public enum PurchaseExecutionKind { Human, Worker }

public sealed class PurchaseDeliveryEvent
{
    private PurchaseDeliveryEvent() { }
    internal PurchaseDeliveryEvent(Guid delivery, PurchaseDeliveryEventKind kind, Guid actor, Guid authorization,
        Guid operation, PurchaseExecutionKind execution, string outcome, Guid? attempt = null,
        long? generation = null, Guid? worker = null, DateTimeOffset? observedAt = null)
    {
        Id = Guid.NewGuid(); DeliveryId = delivery; Kind = kind; ActorId = actor;
        AuthorizationOperationId = authorization; OperationId = operation; ExecutionKind = execution;
        Outcome = outcome; AttemptId = attempt; LeaseGeneration = generation; WorkerInstanceId = worker;
        ObservedAt = observedAt;
    }
    public Guid Id { get; private set; }
    public Guid DeliveryId { get; private set; }
    public Guid? AttemptId { get; private set; }
    public long? LeaseGeneration { get; private set; }
    public PurchaseDeliveryEventKind Kind { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid AuthorizationOperationId { get; private set; }
    public Guid OperationId { get; private set; }
    public PurchaseExecutionKind ExecutionKind { get; private set; }
    public Guid? WorkerInstanceId { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; } // SQL UTC, generated on insertion.
    public DateTimeOffset? ObservedAt { get; private set; }
    public string Outcome { get; private set; } = "";
}
