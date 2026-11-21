namespace Inventario.Api.Infrastructure.Messaging;

public sealed class PurchaseDeliveryOptions
{
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 5;
    public int LeaseSeconds { get; set; } = 120;
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxAttempts { get; set; } = 5;
    internal static bool IsValid(PurchaseDeliveryOptions options) => options.PollSeconds > 0 && options.TimeoutSeconds > 0
        && (long)options.LeaseSeconds > (long)options.TimeoutSeconds + 30 && options.MaxAttempts is >= 1 and <= 5;
}

public sealed record PurchaseMailObservation(bool Accepted, bool Permanent, bool Uncertain,
    string? Code, string? Summary, DateTimeOffset ObservedAt);

public interface IPurchaseMailTransport
{
    Task<PurchaseMailObservation> SendAsync(byte[] message, CancellationToken cancellation);
}
