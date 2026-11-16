namespace Inventario.Api.Domain.Stock;

// Rechazo controlado de un comando de stock. Code y Status siguen el contrato HTTP global.
public sealed class StockOperationException(int status, string code, string message,
    IReadOnlyDictionary<string, string[]>? errors = null) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, string[]> Errors { get; } =
        errors ?? new Dictionary<string, string[]>();
    public Guid? OperationId { get; init; }

    public static StockOperationException Validation(string field, string message) =>
        new(422, "ValidationFailed", message, new Dictionary<string, string[]> { [field] = [message] });

    public static StockOperationException InsufficientStock(string message) => new(409, "InsufficientStock", message);
    public static StockOperationException ReservedConflict(string message) => new(409, "ReservedStockConflict", message);
    public static StockOperationException ArticleInactive(string message) => new(409, "ArticleInactive", message);
    public static StockOperationException AlreadyConfirmed(string message) => new(409, "DocumentAlreadyConfirmed", message);
    public static StockOperationException IdempotencyConflict(string message) => new(409, "IdempotencyConflict", message);
    public static StockOperationException LockTimeout(string message) => new(503, "LockTimeout", message);
}
