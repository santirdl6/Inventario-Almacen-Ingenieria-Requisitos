namespace Inventario.Api.Infrastructure.Http;

public sealed class InventoryProblemException(int status, string code, string message,
    IReadOnlyDictionary<string, string[]>? errors = null) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;

    public static InventoryProblemException Validation(string field, string message) =>
        new(422, "ValidationFailed", message, new Dictionary<string, string[]> { [field] = [message] });
}
