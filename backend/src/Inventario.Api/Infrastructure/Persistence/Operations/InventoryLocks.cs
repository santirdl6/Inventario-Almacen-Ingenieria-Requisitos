using System.Data;
using System.Text.Json;
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

    // HU-02 (FR-021): retirar una zona de recepción serializa el recuento de las que quedan
    // activas. Va en la fase de ubicaciones y después de location:{id}, porque "location:"
    // precede a "locations:" en orden ordinal. StockCore nunca lo adquiere.
    public Task ReceivingZonesAsync(CancellationToken cancellation = default) =>
        AcquireAsync(2, ["locations:receiving"], cancellation);

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
        await using var command = database.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        // One round trip per phase, retaining the exact C# ordinal order through JSON array positions.
        // The timeout remains 5 s PER RESOURCE; a negative result stops the batch and the executor rolls back.
        command.CommandTimeout = checked(30 + pending.Length * 5);
        command.CommandText = """
            DECLARE @result int = 0, @resource nvarchar(255);
            DECLARE resource_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT [value] FROM OPENJSON(@resources) ORDER BY CONVERT(int, [key]);
            OPEN resource_cursor;
            FETCH NEXT FROM resource_cursor INTO @resource;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                EXEC @result = sys.sp_getapplock @Resource = @resource, @LockMode = N'Exclusive',
                    @LockOwner = N'Transaction', @LockTimeout = 5000, @DbPrincipal = N'public';
                IF @result < 0 BREAK;
                FETCH NEXT FROM resource_cursor INTO @resource;
            END;
            CLOSE resource_cursor;
            DEALLOCATE resource_cursor;
            SELECT @result;
            """;
        command.Parameters.Add(new SqlParameter("@resources", SqlDbType.NVarChar, -1) { Value = JsonSerializer.Serialize(pending) });
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellation));
        if (result < 0)
            throw new InventoryProblemException(503, "LockTimeout", "No se pudo completar el bloqueo. Comprueba el resultado antes de repetir.");
        foreach (var resource in pending) held.Add(resource);
        last = pending[^1];
    }

    private static string Id(Guid id) => id != Guid.Empty ? id.ToString("D")
        : throw new InvalidOperationException("Un recurso de bloqueo requiere UUID no vacío.");

    [GeneratedRegex("^[a-z][a-z0-9-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex DocumentType();
}
