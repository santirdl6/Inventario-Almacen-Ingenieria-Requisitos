using System.Collections.ObjectModel;
using System.Text.Json;

namespace Inventario.Api.Infrastructure.Persistence.Operations;

public sealed class OperationResponse : IResult
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public int StatusCode { get; }
    public string? Body { get; }
    public IReadOnlyDictionary<string, string> Headers { get; }
    public Guid? SourceDocumentId { get; }

    public OperationResponse(int statusCode, string? body,
        IReadOnlyDictionary<string, string>? headers = null, Guid? sourceDocumentId = null)
    {
        if (statusCode is < 200 or >= 300 || statusCode == 204 && body is not null)
            throw new InvalidOperationException("Solo se conservan respuestas exitosas y un 204 no tiene cuerpo.");
        if (body is not null)
            using (JsonDocument.Parse(body)) { }
        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in headers ?? new Dictionary<string, string>())
        {
            if (pair.Key is not ("ETag" or "Location") || pair.Value.Contains('\r') || pair.Value.Contains('\n'))
                throw new InvalidOperationException("Solo se conservan ETag y Location como cabeceras del resultado.");
            copy.Add(pair.Key, pair.Value);
        }
        StatusCode = statusCode;
        Body = body;
        Headers = new ReadOnlyDictionary<string, string>(copy);
        SourceDocumentId = sourceDocumentId;
    }

    public static OperationResponse Json(int status, object body, Guid? sourceDocumentId = null,
        IReadOnlyDictionary<string, string>? headers = null) =>
        new(status, JsonSerializer.Serialize(body, JsonOptions), headers, sourceDocumentId);

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCode;
        httpContext.Response.Headers.CacheControl = "no-store";
        foreach (var pair in Headers)
            httpContext.Response.Headers[pair.Key] = pair.Value;
        if (Body is not null)
        {
            httpContext.Response.ContentType = "application/json; charset=utf-8";
            await httpContext.Response.WriteAsync(Body, httpContext.RequestAborted);
        }
    }
}
