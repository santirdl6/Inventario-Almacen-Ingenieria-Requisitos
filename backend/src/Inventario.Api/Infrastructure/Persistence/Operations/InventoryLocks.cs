using System.Text.RegularExpressions;
using Inventario.Api.Infrastructure.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventario.Api.Infrastructure.Persistence.Operations;

public sealed record DocumentLock(string Type, Guid Id);

public sealed partial class InventoryLocks(InventoryDbContext database)
{
    private readonly HashSet<string> held = new(StringComparer.Ordinal);
    private int phase = -1;
    private string? last;

    internal Task OperationAsync(Guid operation, CancellationToken cancellation) =>
        AcquireAsync(0, [$"operation:{Id(operation)}"], cancellation);

    public Task DocumentsAsync(IEnumerable<DocumentLock> documents, CancellationToken cancellation = default) =>
        AcquireAsync(1, documents.Select(document =>
        {
            if (!DocumentType().IsMatch(document.Type))
                throw new InvalidOperationException("El tipo de documento no tiene formato canónico.");
            return $"document:{document.Type}:{Id(document.Id)}";
        }), cancellation);

    public Task LocationsAsync(IEnumerable<Guid> locations, CancellationToken cancellation = default) =>
        AcquireAsync(2, locations.Select(id => $"location:{Id(id)}"), cancellation);

    public Task ArticlesAsync(IEnumerable<Guid> articles, CancellationToken cancellation = default) =>
        AcquireAsync(3, articles.Select(id => $"article:{Id(id)}"), cancellation);

    private async Task AcquireAsync(int requestedPhase, IEnumerable<string> resources, CancellationToken cancellation)
    {
        var pending = resources.Distinct(StringComparer.Ordinal).Where(resource => !held.Contains(resource))
            .Order(StringComparer.Ordinal).ToArray();
        if (pending.Length == 0) return;
        if (requestedPhase < phase || requestedPhase == phase && last is not null
            && StringComparer.Ordinal.Compare(pending[0], last) < 0)
            throw new InvalidOperationException("Los bloqueos requieren replanificar: operación → documentos → ubicaciones → artículos, orden ordinal dentro de cada fase.");
        var transaction = database.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Los bloqueos requieren una transacción activa del comando.");
        if (requestedPhase != phase) last = null;
        phase = requestedPhase;
        foreach (var resource in pending)
        {
            await using var command = database.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = """
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource = @resource, @LockMode = N'Exclusive',
                    @LockOwner = N'Transaction', @LockTimeout = 5000, @DbPrincipal = N'public';
                SELECT @result;
                """;
            command.Parameters.Add(new SqlParameter("@resource", resource));
            var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellation));
            if (result < 0)
                throw new InventoryProblemException(503, "LockTimeout", "No se pudo completar el bloqueo. Comprueba el resultado antes de repetir.");
            held.Add(resource);
            last = resource;
        }
    }

    private static string Id(Guid id) => id != Guid.Empty ? id.ToString("D")
        : throw new InvalidOperationException("Un recurso de bloqueo requiere UUID no vacío.");

    [GeneratedRegex("^[a-z][a-z0-9-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex DocumentType();
}
