using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Inventario.IntegrationTests.Stock;

// Inyecta un fallo después de ejecutar el SQL de SaveChanges (filas escritas dentro de la
// transacción del ejecutor) y antes del commit. Se arma con el número de fallos deseado.
internal sealed class FailAfterSaveInterceptor : SaveChangesInterceptor
{
    private int remaining;

    public void Arm(int failures = 1) => Interlocked.Exchange(ref remaining, failures);

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Decrement(ref remaining) >= 0)
            throw new InvalidOperationException("Fallo simulado tras escribir y antes del commit.");
        return ValueTask.FromResult(result);
    }
}
