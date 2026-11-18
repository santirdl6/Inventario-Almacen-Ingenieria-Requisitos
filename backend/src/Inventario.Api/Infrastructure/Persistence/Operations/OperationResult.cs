using System.Text.Json;

namespace Inventario.Api.Infrastructure.Persistence.Operations;

public sealed class OperationResult
{
    private OperationResult() { }
    public Guid OperationId { get; private set; }
    public Guid ActorId { get; private set; }
    public string RequestHash { get; private set; } = "";
    public string Method { get; private set; } = "";
    public string Resource { get; private set; } = "";
    public string RequiredPermission { get; private set; } = "";
    public int ResultStatus { get; private set; }
    public string? ResultBody { get; private set; }
    public string ResultHeaders { get; private set; } = "{}";
    public Guid? SourceDocumentId { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    internal static OperationResult Confirmed(OperationRequest request, Guid actorId, string hash, OperationResponse response) => new()
    {
        OperationId = request.OperationId, ActorId = actorId, RequestHash = hash,
        Method = request.Method.ToUpperInvariant(), Resource = RequestFingerprint.CanonicalResource(request.Resource),
        RequiredPermission = request.RequiredPermission, ResultStatus = response.StatusCode, ResultBody = response.Body,
        ResultHeaders = JsonSerializer.Serialize(response.Headers), SourceDocumentId = response.SourceDocumentId,
        RecordedAt = DateTimeOffset.UtcNow
    };

    public OperationResponse Response() => new(ResultStatus, ResultBody,
        JsonSerializer.Deserialize<Dictionary<string, string>>(ResultHeaders), SourceDocumentId);
}

public sealed record OperationRequest(Guid OperationId, string Method, string Resource,
    string RequiredPermission, JsonElement NormalizedBody);
