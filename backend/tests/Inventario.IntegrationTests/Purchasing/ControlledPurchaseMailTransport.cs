using System.Collections.Concurrent;

namespace Inventario.IntegrationTests.Purchasing;

// Test-only transport port. The production adapter can delegate to this script without
// introducing failure switches or test endpoints in the deployed application.
internal sealed class ControlledPurchaseMailTransport
{
    private readonly ConcurrentQueue<Func<CancellationToken, Task>> steps = new();
    public ConcurrentQueue<byte[]> Messages { get; } = new();
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Enqueue(Func<CancellationToken, Task> step) => steps.Enqueue(step);

    public async Task SendAsync(byte[] message, CancellationToken cancellation)
    {
        Messages.Enqueue(message.ToArray());
        Entered.TrySetResult();
        if (steps.TryDequeue(out var step)) await step(cancellation);
    }

    public void PauseNext() => Enqueue(async cancellation => await Release.Task.WaitAsync(cancellation));
}
