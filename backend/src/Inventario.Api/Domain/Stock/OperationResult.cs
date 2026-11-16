namespace Inventario.Api.Domain.Stock;

// Resultado idempotente de una mutación. Solo se persiste junto al commit de sus efectos.
public sealed class OperationResult
{
    public const int MethodMaxLength = 16;
    public const int ResourceMaxLength = 256;

    private OperationResult() { }

    internal OperationResult(Guid operationId, Guid actorId, string requestHash, string method,
        string resource, int resultStatus, string resultBody, Guid? sourceDocumentId, DateTimeOffset recordedAt)
    {
        OperationId = operationId;
        ActorId = actorId;
        RequestHash = requestHash;
        Method = method;
        Resource = resource;
        ResultStatus = resultStatus;
        ResultBody = resultBody;
        SourceDocumentId = sourceDocumentId;
        RecordedAt = recordedAt;
    }

    public Guid OperationId { get; private set; }
    public Guid ActorId { get; private set; }
    public string RequestHash { get; private set; } = "";
    public string Method { get; private set; } = "";
    public string Resource { get; private set; } = "";
    public int ResultStatus { get; private set; }
    public string ResultBody { get; private set; } = "";
    public Guid? SourceDocumentId { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
}
