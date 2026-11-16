using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventario.Api.Domain.Identity;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventario.Api.Features.Stock.Core;

// Núcleo de stock H0 (propuesta de B). Aplica el protocolo de
// .specify/memory/project/contracts/stock-operations.md: transacción única por intento,
// applock de operación → documento → ubicaciones → artículos, lectura tras los bloqueos
// y persistencia conjunta de diario, saldos y OperationResult.
//
// El núcleo identifica al actor, pero NO autoriza: cada endpoint/handler de HU-01/03/05
// comprueba su permiso antes de invocarlo. Tampoco hace commit fuera de su propia transacción.
public sealed class StockCore(InventoryDbContext database, ICurrentActor currentActor)
{
    internal const int LockTimeoutMilliseconds = 5000;
    private const int MaximumLines = 200;
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

    // Punto de inyección de fallos para pruebas: se ejecuta tras escribir y antes del commit.
    internal Func<CancellationToken, Task>? BeforeCommit { get; set; }

    public Task<StockOperationOutcome> ReceiveAsync(StockCommandContext context,
        IReadOnlyList<StockAllocation> allocations, CancellationToken cancellationToken = default) =>
        ExecuteAsync(StockOperation.Receive, context, Simple(allocations), cancellationToken);

    public Task<StockOperationOutcome> ReserveAsync(StockCommandContext context,
        IReadOnlyList<StockAllocation> allocations, CancellationToken cancellationToken = default) =>
        ExecuteAsync(StockOperation.Reserve, context, Simple(allocations), cancellationToken);

    public Task<StockOperationOutcome> ReleaseAsync(StockCommandContext context,
        IReadOnlyList<StockAllocation> allocations, CancellationToken cancellationToken = default) =>
        ExecuteAsync(StockOperation.Release, context, Simple(allocations), cancellationToken);

    public Task<StockOperationOutcome> ShipAsync(StockCommandContext context,
        IReadOnlyList<ShipmentAllocation> allocations, CancellationToken cancellationToken = default) =>
        ExecuteAsync(StockOperation.Ship, context,
            allocations.Select(line => new Line(line.ArticleId, line.LocationId, line.LotCode,
                line.Quantity, line.OwnReservedToConsume)).ToList(), cancellationToken);

    private static List<Line> Simple(IReadOnlyList<StockAllocation> allocations) =>
        allocations.Select(line => new Line(line.ArticleId, line.LocationId, line.LotCode, line.Quantity, 0m)).ToList();

    private async Task<StockOperationOutcome> ExecuteAsync(StockOperation operation,
        StockCommandContext context, List<Line> lines, CancellationToken cancellationToken)
    {
        // 1. Validar forma antes de abrir transacción. La autorización la hace el handler.
        var command = Normalize(operation, context, lines);
        var actorId = await currentActor.GetRequiredActorIdAsync(cancellationToken);
        var hash = Hash(command);
        var resource = $"stock/{operation}/{command.SourceType}/{command.SourceId:D}".ToLowerInvariant();

        // 2. Una transacción nueva y un ChangeTracker limpio por intento dentro de la estrategia.
        var strategy = database.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            token => AttemptAsync(command, actorId, hash, resource, token), cancellationToken);
    }

    private async Task<StockOperationOutcome> AttemptAsync(Command command, Guid actorId, string hash,
        string resource, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        await using var transaction = await database.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        // 3. Operación idempotente.
        await AcquireAsync($"operation:{command.OperationId:D}", cancellationToken);
        var previous = await database.Set<OperationResult>().AsNoTracking()
            .SingleOrDefaultAsync(result => result.OperationId == command.OperationId, cancellationToken);
        if (previous is not null)
        {
            if (previous.ActorId != actorId || previous.RequestHash != hash || previous.Resource != resource)
                throw new StockOperationException(409, "IdempotencyConflict",
                    "La clave de idempotencia ya se usó con otro contenido, recurso o usuario.")
                { OperationId = command.OperationId };
            var stored = JsonSerializer.Deserialize<StockOperationOutcome>(previous.ResultBody)!;
            await transaction.CommitAsync(cancellationToken);
            return stored with { Replayed = true };
        }

        // 4–6. Documento, ubicaciones y artículos, cada grupo en orden de ID.
        await AcquireAsync($"document:{command.SourceType}:{command.SourceId:D}", cancellationToken);
        var locationIds = command.Lines.Select(line => line.LocationId).Distinct().Order().ToList();
        foreach (var locationId in locationIds)
            await AcquireAsync($"location:{locationId:D}", cancellationToken);
        var articleIds = command.Lines.Select(line => line.ArticleId).Distinct().Order().ToList();
        foreach (var articleId in articleIds)
            await AcquireAsync($"article:{articleId:D}", cancellationToken);

        // 7. Leer el estado actual después de los bloqueos.
        var kind = command.Operation.ToMovementKind();
        if (await database.Set<StockMovement>().AnyAsync(movement =>
                movement.SourceDocumentType == command.SourceType
                && movement.SourceDocumentId == command.SourceId
                && movement.Kind == kind, cancellationToken))
            throw StockOperationException.AlreadyConfirmed("Este hecho del documento ya se aplicó al stock.");

        var articles = await database.Articles
            .Where(article => articleIds.Contains(article.Id)).ToDictionaryAsync(article => article.Id, cancellationToken);
        var locations = await database.Set<Location>()
            .Where(location => locationIds.Contains(location.Id)).ToDictionaryAsync(location => location.Id, cancellationToken);
        var resolved = command.Lines.Select((line, index) => Resolve(command.Operation, line, index, articles, locations)).ToList();

        var lotCodes = resolved.Select(line => line.LotCode).Distinct().ToList();
        var lots = await database.Set<Lot>()
            .Where(lot => articleIds.Contains(lot.ArticleId) && lotCodes.Contains(lot.Code))
            .ToListAsync(cancellationToken);

        // 8. Validar invariantes y preparar filas; persistir todo junto.
        var recordedAt = DateTimeOffset.UtcNow;
        var movement = new StockMovement(kind, command.SourceType, command.SourceId, command.Reason, actorId,
            command.OccurredAt, recordedAt, command.OperationId, command.SupportReference,
            command.SupportNotApplicableReason, command.CounterpartyReference);
        database.Add(movement);

        foreach (var line in resolved)
        {
            var lot = lots.SingleOrDefault(lot => lot.ArticleId == line.Article.Id && lot.Code == line.LotCode);
            if (lot is null)
            {
                if (command.Operation != StockOperation.Receive)
                    throw StockOperationException.InsufficientStock(
                        $"No hay existencias de {line.Article.Sku} en {line.Location.Code} con lote {line.LotCode}.");
                lot = line.Article.TracksLot ? Lot.Tracked(line.Article.Id, line.LotCode) : Lot.Untracked(line.Article.Id);
                database.Add(lot);
                lots.Add(lot);
            }

            var balance = await database.Set<StockBalance>()
                .FindAsync([line.Article.Id, line.Location.Id, lot.Id], cancellationToken);
            if (balance is null)
            {
                if (command.Operation != StockOperation.Receive)
                    throw StockOperationException.InsufficientStock(
                        $"No hay existencias de {line.Article.Sku} en {line.Location.Code} con lote {lot.Code}.");
                balance = new StockBalance(line.Article.Id, line.Location.Id, lot.Id);
                database.Add(balance);
            }

            foreach (var (effect, quantity) in Effects(command.Operation, line, balance))
            {
                var entry = new StockEntry(movement.Id, line.Article.Id, line.Location.Id, lot.Id,
                    quantity, line.Article.BaseUnit, effect);
                movement.AddEntry(entry);
                balance.Apply(entry.PhysicalDelta, entry.ReservedDelta);
            }
        }

        var outcome = new StockOperationOutcome(command.OperationId, movement.Id, command.SourceType,
            command.SourceId, recordedAt);
        database.Add(new OperationResult(command.OperationId, actorId, hash, "POST", resource, 200,
            JsonSerializer.Serialize(outcome), command.SourceId, recordedAt));
        await database.SaveChangesAsync(cancellationToken);

        if (BeforeCommit is not null)
            await BeforeCommit(cancellationToken);

        // 9. Solo hay éxito tras el commit de todos los efectos.
        await transaction.CommitAsync(cancellationToken);
        return outcome;
    }

    private static IEnumerable<(StockEffect Effect, decimal Quantity)> Effects(StockOperation operation,
        ResolvedLine line, StockBalance balance)
    {
        var where = $"{line.Article.Sku} en {line.Location.Code}, lote {line.LotCode}";
        switch (operation)
        {
            case StockOperation.Receive:
                return [(StockEffect.Receive, line.Quantity)];
            case StockOperation.Reserve:
                if (line.Quantity > balance.Available)
                    throw StockOperationException.InsufficientStock(
                        $"Disponible insuficiente para reservar {Format(line.Quantity)} de {where} (disponible {Format(balance.Available)}).");
                return [(StockEffect.Reserve, line.Quantity)];
            case StockOperation.Release:
                if (line.Quantity > balance.Reserved)
                    throw StockOperationException.ReservedConflict(
                        $"No se pueden liberar {Format(line.Quantity)} de {where}: solo hay {Format(balance.Reserved)} reservado.");
                return [(StockEffect.Release, line.Quantity)];
            case StockOperation.Ship:
                // Consumir = Rpropia indicada; SinReserva = Q − Consumir ≤ D y Q ≤ F.
                var consume = line.OwnReserved;
                var unreserved = line.Quantity - consume;
                if (consume > balance.Reserved)
                    throw StockOperationException.ReservedConflict(
                        $"La reserva propia indicada para {where} supera lo reservado en el saldo.");
                if (line.Quantity > balance.Physical || unreserved > balance.Available)
                    throw StockOperationException.InsufficientStock(
                        $"No se pueden servir {Format(line.Quantity)} de {where} (físico {Format(balance.Physical)}, disponible {Format(balance.Available)}).");
                var effects = new List<(StockEffect, decimal)>();
                if (consume > 0) effects.Add((StockEffect.ShipReserved, consume));
                if (unreserved > 0) effects.Add((StockEffect.ShipAvailable, unreserved));
                return effects;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static ResolvedLine Resolve(StockOperation operation, Line line, int index,
        Dictionary<Guid, Article> articles, Dictionary<Guid, Location> locations)
    {
        if (!articles.TryGetValue(line.ArticleId, out var article))
            throw StockOperationException.Validation($"lines[{index}].articleId", "El artículo no existe.");
        if (!locations.TryGetValue(line.LocationId, out var location))
            throw StockOperationException.Validation($"lines[{index}].locationId", "La ubicación no existe.");

        // R-05: un artículo desactivado no admite nuevas recepciones ni reservas,
        // pero sí liberar y servir compromisos existentes.
        if (!article.IsActive && operation is StockOperation.Receive or StockOperation.Reserve)
            throw StockOperationException.ArticleInactive($"El artículo {article.Sku} está desactivado.");
        if (!location.IsActive && operation is StockOperation.Receive or StockOperation.Reserve)
            throw StockOperationException.Validation($"lines[{index}].locationId", $"La ubicación {location.Code} está desactivada.");

        if (!article.AllowsFractional
            && (decimal.Truncate(line.Quantity) != line.Quantity || decimal.Truncate(line.OwnReserved) != line.OwnReserved))
            throw StockOperationException.Validation($"lines[{index}].quantity",
                $"El artículo {article.Sku} se gestiona en unidades enteras de {article.BaseUnit}.");

        string lotCode;
        if (article.TracksLot)
        {
            lotCode = line.LotCode ?? throw StockOperationException.Validation($"lines[{index}].lotCode",
                $"El artículo {article.Sku} exige lote.");
            if (lotCode == Lot.UntrackedCode)
                throw StockOperationException.Validation($"lines[{index}].lotCode",
                    "SIN-LOTE no es válido para un artículo con seguimiento de lote.");
        }
        else
        {
            if (line.LotCode is not null && line.LotCode != Lot.UntrackedCode)
                throw StockOperationException.Validation($"lines[{index}].lotCode",
                    $"El artículo {article.Sku} no tiene seguimiento de lote.");
            lotCode = Lot.UntrackedCode;
        }

        return new ResolvedLine(article, location, lotCode, line.Quantity, line.OwnReserved);
    }

    private static Command Normalize(StockOperation operation, StockCommandContext context, List<Line> lines)
    {
        if (context.OperationId == Guid.Empty)
            throw StockOperationException.Validation("operationId", "La clave de idempotencia es obligatoria.");
        if (context.Source is null || string.IsNullOrWhiteSpace(context.Source.Type) || context.Source.Id == Guid.Empty)
            throw StockOperationException.Validation("source", "El documento origen es obligatorio.");
        var sourceType = context.Source.Type.Trim();
        if (sourceType.Length > StockMovement.DocumentTypeMaxLength)
            throw StockOperationException.Validation("source", "El tipo de documento es demasiado largo.");
        var reason = Optional(context.Reason, StockMovement.ReasonMaxLength, "reason")
            ?? throw StockOperationException.Validation("reason", "El motivo es obligatorio.");
        var support = Optional(context.SupportReference, StockMovement.ReferenceMaxLength, "supportReference");
        var notApplicable = Optional(context.SupportNotApplicableReason, StockMovement.ReasonMaxLength,
            "supportNotApplicableReason");
        if (support is null && notApplicable is null)
            throw StockOperationException.Validation("supportReference",
                "Indica la referencia de soporte o por qué no aplica.");
        var counterparty = Optional(context.CounterpartyReference, StockMovement.ReferenceMaxLength,
            "counterpartyReference");
        var occurredAt = context.OccurredAt.ToUniversalTime();
        if (occurredAt == default || occurredAt > DateTimeOffset.UtcNow + AllowedClockSkew)
            throw StockOperationException.Validation("occurredAt", "La fecha del hecho no puede ser futura.");

        if (lines.Count == 0 || lines.Count > MaximumLines)
            throw StockOperationException.Validation("lines", $"Indica entre 1 y {MaximumLines} líneas.");
        var normalized = new List<Line>(lines.Count);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.ArticleId == Guid.Empty)
                throw StockOperationException.Validation($"lines[{index}].articleId", "El artículo es obligatorio.");
            if (line.LocationId == Guid.Empty)
                throw StockOperationException.Validation($"lines[{index}].locationId", "La ubicación es obligatoria.");
            if (line.Quantity <= 0 || decimal.Round(line.Quantity, 3) != line.Quantity || line.Quantity >= 1_000_000_000_000_000m)
                throw StockOperationException.Validation($"lines[{index}].quantity",
                    "La cantidad debe ser positiva, con un máximo de tres decimales.");
            if (line.OwnReserved < 0 || line.OwnReserved > line.Quantity || decimal.Round(line.OwnReserved, 3) != line.OwnReserved)
                throw StockOperationException.Validation($"lines[{index}].ownReservedToConsume",
                    "La reserva propia a consumir debe estar entre cero y la cantidad.");
            string? lotCode;
            try { lotCode = Lot.NormalizeCode(line.LotCode); }
            catch (ArgumentException error)
            { throw StockOperationException.Validation($"lines[{index}].lotCode", error.Message); }
            normalized.Add(line with { LotCode = lotCode });
        }

        // Un bucket aparece una sola vez por comando: evita aplicar validaciones sobre saldos parciales.
        if (normalized.GroupBy(line => (line.ArticleId, line.LocationId, line.LotCode ?? Lot.UntrackedCode))
            .Any(group => group.Count() > 1))
            throw StockOperationException.Validation("lines", "Cada artículo, ubicación y lote debe aparecer una sola vez.");

        return new Command(operation, context.OperationId, sourceType, context.Source.Id, occurredAt, reason,
            support, notApplicable, counterparty, normalized);
    }

    private static string? Optional(string? value, int maximumLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw StockOperationException.Validation(field, $"No puede superar {maximumLength} caracteres.");
        return normalized;
    }

    private static string Hash(Command command)
    {
        // Huella del cuerpo normalizado: 6 y 6.000 son la misma cantidad.
        var canonical = new
        {
            operation = command.Operation.ToString(),
            sourceType = command.SourceType.ToLowerInvariant(),
            sourceId = command.SourceId,
            occurredAt = command.OccurredAt.ToString("O", CultureInfo.InvariantCulture),
            command.Reason,
            command.SupportReference,
            command.SupportNotApplicableReason,
            command.CounterpartyReference,
            lines = command.Lines.Select(line => new
            {
                line.ArticleId,
                line.LocationId,
                line.LotCode,
                quantity = Format(line.Quantity),
                ownReserved = Format(line.OwnReserved)
            })
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical))));
    }

    private static string Format(decimal value) => value.ToString("0.000", CultureInfo.InvariantCulture);

    private async Task AcquireAsync(string resource, CancellationToken cancellationToken)
    {
        var connection = database.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = database.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = @resource, @LockMode = N'Exclusive',
                @LockOwner = N'Transaction', @LockTimeout = @timeout;
            SELECT @result;
            """;
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = resource.ToLowerInvariant() });
        command.Parameters.Add(new SqlParameter("@timeout", SqlDbType.Int) { Value = LockTimeoutMilliseconds });
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (result < 0)
        {
            // Timeout (-1), cancelación (-2), deadlock (-3) o error (-999): rollback explícito.
            await database.Database.RollbackTransactionAsync(cancellationToken);
            throw StockOperationException.LockTimeout(
                "El stock está ocupado por otra operación. Comprueba el resultado de la operación antes de repetirla.");
        }
    }

    private sealed record Line(Guid ArticleId, Guid LocationId, string? LotCode, decimal Quantity, decimal OwnReserved);

    private sealed record ResolvedLine(Article Article, Location Location, string LotCode, decimal Quantity, decimal OwnReserved);

    private sealed record Command(StockOperation Operation, Guid OperationId, string SourceType, Guid SourceId,
        DateTimeOffset OccurredAt, string Reason, string? SupportReference, string? SupportNotApplicableReason,
        string? CounterpartyReference, IReadOnlyList<Line> Lines);
}

internal enum StockOperation
{
    Receive,
    Reserve,
    Release,
    Ship
}

internal static class StockOperationExtensions
{
    public static MovementKind ToMovementKind(this StockOperation operation) => operation switch
    {
        StockOperation.Receive => Domain.Stock.MovementKind.Receipt,
        StockOperation.Reserve => Domain.Stock.MovementKind.Reservation,
        StockOperation.Release => Domain.Stock.MovementKind.Release,
        StockOperation.Ship => Domain.Stock.MovementKind.Shipment,
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };
}

public static class StockCoreRegistration
{
    public static IServiceCollection AddStockCore(this IServiceCollection services) =>
        services.AddScoped<StockCore>();
}
