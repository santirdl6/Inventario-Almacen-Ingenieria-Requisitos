namespace Inventario.Api.Domain.MasterData;

public enum MasterDataAction { Create, Update, Deactivate, Reactivate, Delete }
public enum MasterDataEntity { Family, Supplier, Article }

public sealed class MasterDataAudit
{
    private MasterDataAudit() { }
    internal MasterDataAudit(MasterDataEntity entity, Guid entityId, MasterDataAction action,
        string? before, string? after, Guid actorId, Guid operationId)
    {
        if (entityId == Guid.Empty || actorId == Guid.Empty || operationId == Guid.Empty
            || !Enum.IsDefined(entity) || !Enum.IsDefined(action))
            throw new InvalidOperationException("La auditoría requiere entidad, acción, actor y operación válidos.");
        EntityType = entity;
        EntityId = entityId;
        Action = action;
        Before = before;
        After = after;
        ActorId = actorId;
        OperationId = operationId;
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public MasterDataEntity EntityType { get; private set; }
    public Guid EntityId { get; private set; }
    public MasterDataAction Action { get; private set; }
    public string? Before { get; private set; }
    public string? After { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid OperationId { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; } = DateTimeOffset.UtcNow;
}
