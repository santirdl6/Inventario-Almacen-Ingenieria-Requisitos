using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Stock.Core;

// Núcleo de stock H0 (B) sobre la base común de A
// (.specify/memory/project/contracts/stock-operations.md).
//
// No abre transacción, no guarda resultados ni toma el bloqueo de operación: se ejecuta como
// paso de dominio dentro de InventoryOperationExecutor, que autentica, autoriza el permiso de
// la OperationRequest, resuelve la idempotencia con el único OperationResult y confirma todo
// en un commit. Aquí se siguen los pasos 4–8 del protocolo con InventoryLocks:
// documento → ubicaciones → artículos, lectura tras los bloqueos y escritura de diario,
// saldos y primera referencia del artículo (IsDefinitionLocked) en la misma transacción.
public sealed partial class StockCore
{
    public const int MaximumLines = 200;
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

    // Valida y normaliza. El cuerpo normalizado es el que el consumidor pasa en OperationRequest,
    // para que 6 y 6.000 o un lote en minúsculas sean la misma intención (R-04).
    public static StockCommand Prepare(StockOperation operation, StockCommandContext context,
        IReadOnlyList<StockAllocation> allocations)
    {
        if (operation == StockOperation.Ship)
            throw new ArgumentException("Una salida usa ShipmentAllocation con su reserva propia.", nameof(operation));
        return Normalize(operation, context, allocations.Select(line =>
            new StockLine(line.ArticleId, line.LocationId, line.LotCode, line.Quantity, 0m)).ToList());
    }

    public static StockCommand PrepareShipment(StockCommandContext context, IReadOnlyList<ShipmentAllocation> allocations) =>
        Normalize(StockOperation.Ship, context, allocations.Select(line =>
            new StockLine(line.ArticleId, line.LocationId, line.LotCode, line.Quantity, line.OwnReservedToConsume)).ToList());

    public static OperationResponse Response(StockOperationOutcome outcome) =>
        OperationResponse.Json(200, outcome, outcome.SourceDocumentId);

    public static StockOperationOutcome Outcome(OperationResponse response) =>
        JsonSerializer.Deserialize<StockOperationOutcome>(response.Body
            ?? throw new InvalidOperationException("Un resultado de stock siempre tiene cuerpo."), OperationResponse.JsonOptions)!;

    public async Task<StockOperationOutcome> ApplyAsync(OperationContext context, StockCommand command,
        CancellationToken cancellationToken = default)
    {
        var database = context.Database;
        // Después del replay del ejecutor: las intenciones históricas siguen siendo recuperables.
        if (command.CounterpartyReference is null && command.CounterpartyNotApplicableReason is null)
            throw StockProblems.Validation("counterpartyReference", "Indica la contraparte o explica por qué no aplica.");

        // 4–6. Documento, ubicaciones y artículos; InventoryLocks ordena cada fase y rechaza saltos.
        await context.Locks.DocumentsAsync([new DocumentLock(command.SourceType, command.SourceId)], cancellationToken);
        var locationIds = command.Lines.Select(line => line.LocationId).Distinct().ToList();
        await context.Locks.LocationsAsync(locationIds, cancellationToken);
        var articleIds = command.Lines.Select(line => line.ArticleId).Distinct().ToList();
        await context.Locks.ArticlesAsync(articleIds, cancellationToken);

        // 7. Leer el estado actual después de los bloqueos.
        var kind = command.Operation.ToMovementKind();
        if (await database.Set<StockMovement>().AnyAsync(movement =>
                movement.SourceDocumentType == command.SourceType
                && movement.SourceDocumentId == command.SourceId
                && movement.Kind == kind, cancellationToken))
            throw StockProblems.AlreadyConfirmed("Este hecho del documento ya se aplicó al stock.");

        var articles = await database.Articles
            .Where(article => articleIds.Contains(article.Id)).ToDictionaryAsync(article => article.Id, cancellationToken);
        var locations = await database.Set<Location>()
            .Where(location => locationIds.Contains(location.Id)).ToDictionaryAsync(location => location.Id, cancellationToken);
        var resolved = command.Lines.Select((line, index) => Resolve(command.Operation, line, index, articles, locations)).ToList();

        // Primera referencia (data-model, Article): IsDefinitionLocked pasa a true con el primer
        // movimiento o reserva, bajo el mismo bloqueo de artículo y en la misma transacción.
        // Se reutiliza el tracker de A; el bloqueo ya está tomado y no se vuelve a pedir.
        await context.Services.GetRequiredService<ArticleReferenceTracker>().MarkAsync(context, articleIds, cancellationToken);

        var lotCodes = resolved.Select(line => line.LotCode).Distinct().ToList();
        var lots = await database.Set<Lot>()
            .Where(lot => articleIds.Contains(lot.ArticleId) && lotCodes.Contains(lot.Code))
            .ToListAsync(cancellationToken);

        // 8. Validar invariantes y preparar filas; el ejecutor persiste todo con el OperationResult.
        var recordedAt = DateTimeOffset.UtcNow;
        var movement = new StockMovement(kind, command.SourceType, command.SourceId, command.Reason, context.ActorId,
            command.OccurredAt, recordedAt, context.OperationId, command.SupportReference,
            command.SupportNotApplicableReason, command.CounterpartyReference, command.CounterpartyNotApplicableReason);
        database.Add(movement);

        foreach (var line in resolved)
        {
            var lot = lots.SingleOrDefault(lot => lot.ArticleId == line.Article.Id && lot.Code == line.LotCode);
            if (lot is null)
            {
                if (command.Operation != StockOperation.Receive)
                    throw StockProblems.InsufficientStock(
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
                    throw StockProblems.InsufficientStock(
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

        return new StockOperationOutcome(context.OperationId, movement.Id, command.SourceType, command.SourceId, recordedAt);
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
                    throw StockProblems.InsufficientStock(
                        $"Disponible insuficiente para reservar {Format(line.Quantity)} de {where} (disponible {Format(balance.Available)}).");
                return [(StockEffect.Reserve, line.Quantity)];
            case StockOperation.Release:
                if (line.Quantity > balance.Reserved)
                    throw StockProblems.ReservedConflict(
                        $"No se pueden liberar {Format(line.Quantity)} de {where}: solo hay {Format(balance.Reserved)} reservado.");
                return [(StockEffect.Release, line.Quantity)];
            case StockOperation.Ship:
                // Consumir = Rpropia indicada; SinReserva = Q − Consumir ≤ D y Q ≤ F.
                var consume = line.OwnReserved;
                var unreserved = line.Quantity - consume;
                if (consume > balance.Reserved)
                    throw StockProblems.ReservedConflict(
                        $"La reserva propia indicada para {where} supera lo reservado en el saldo.");
                if (line.Quantity > balance.Physical || unreserved > balance.Available)
                    throw StockProblems.InsufficientStock(
                        $"No se pueden servir {Format(line.Quantity)} de {where} (físico {Format(balance.Physical)}, disponible {Format(balance.Available)}).");
                var effects = new List<(StockEffect, decimal)>();
                if (consume > 0) effects.Add((StockEffect.ShipReserved, consume));
                if (unreserved > 0) effects.Add((StockEffect.ShipAvailable, unreserved));
                return effects;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static ResolvedLine Resolve(StockOperation operation, StockLine line, int index,
        Dictionary<Guid, Article> articles, Dictionary<Guid, Location> locations)
    {
        if (!articles.TryGetValue(line.ArticleId, out var article))
            throw StockProblems.Validation($"lines[{index}].articleId", "El artículo no existe.");
        if (!locations.TryGetValue(line.LocationId, out var location))
            throw StockProblems.Validation($"lines[{index}].locationId", "La ubicación no existe.");

        // R-05: un artículo desactivado no admite nuevas recepciones ni reservas,
        // pero sí liberar y servir compromisos existentes.
        if (!article.IsActive && operation is StockOperation.Receive or StockOperation.Reserve)
            throw StockProblems.ArticleInactive($"El artículo {article.Sku} está desactivado.");
        if (!location.IsActive && operation is StockOperation.Receive or StockOperation.Reserve)
            throw StockProblems.Validation($"lines[{index}].locationId", $"La ubicación {location.Code} está desactivada.");

        if (!article.AllowsFractional
            && (decimal.Truncate(line.Quantity) != line.Quantity || decimal.Truncate(line.OwnReserved) != line.OwnReserved))
            throw StockProblems.Validation($"lines[{index}].quantity",
                $"El artículo {article.Sku} se gestiona en unidades enteras de {article.BaseUnit}.");

        string lotCode;
        if (article.TracksLot)
        {
            lotCode = line.LotCode ?? throw StockProblems.Validation($"lines[{index}].lotCode",
                $"El artículo {article.Sku} exige lote.");
            if (lotCode == Lot.UntrackedCode)
                throw StockProblems.Validation($"lines[{index}].lotCode",
                    "SIN-LOTE no es válido para un artículo con seguimiento de lote.");
        }
        else
        {
            if (line.LotCode is not null && line.LotCode != Lot.UntrackedCode)
                throw StockProblems.Validation($"lines[{index}].lotCode",
                    $"El artículo {article.Sku} no tiene seguimiento de lote.");
            lotCode = Lot.UntrackedCode;
        }

        return new ResolvedLine(article, location, lotCode, line.Quantity, line.OwnReserved);
    }

    private static StockCommand Normalize(StockOperation operation, StockCommandContext context, List<StockLine> lines)
    {
        if (context.Source is null || string.IsNullOrWhiteSpace(context.Source.Type) || context.Source.Id == Guid.Empty)
            throw StockProblems.Validation("source", "El documento origen es obligatorio.");
        var sourceType = context.Source.Type.Trim().ToLowerInvariant();
        // Mismo formato que el bloqueo document:{type}:{id} de InventoryLocks.
        if (!DocumentType().IsMatch(sourceType))
            throw StockProblems.Validation("source", "El tipo de documento debe ser canónico (minúsculas, números y guiones; máximo 32).");
        var reason = Optional(context.Reason, StockMovement.ReasonMaxLength, "reason")
            ?? throw StockProblems.Validation("reason", "El motivo es obligatorio.");
        var support = Optional(context.SupportReference, StockMovement.ReferenceMaxLength, "supportReference");
        var notApplicable = Optional(context.SupportNotApplicableReason, StockMovement.ReasonMaxLength,
            "supportNotApplicableReason");
        if (support is null && notApplicable is null)
            throw StockProblems.Validation("supportReference", "Indica la referencia de soporte o por qué no aplica.");
        var counterparty = Optional(context.CounterpartyReference, StockMovement.ReferenceMaxLength,
            "counterpartyReference");
        var counterpartyNotApplicable = Optional(context.CounterpartyNotApplicableReason, StockMovement.ReasonMaxLength,
            "counterpartyNotApplicableReason");
        var occurredAt = context.OccurredAt.ToUniversalTime();
        if (occurredAt == default || occurredAt > DateTimeOffset.UtcNow + AllowedClockSkew)
            throw StockProblems.Validation("occurredAt", "La fecha del hecho no puede ser futura.");

        if (lines.Count == 0 || lines.Count > MaximumLines)
            throw StockProblems.Validation("lines", $"Indica entre 1 y {MaximumLines} líneas.");
        var normalized = new List<StockLine>(lines.Count);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.ArticleId == Guid.Empty)
                throw StockProblems.Validation($"lines[{index}].articleId", "El artículo es obligatorio.");
            if (line.LocationId == Guid.Empty)
                throw StockProblems.Validation($"lines[{index}].locationId", "La ubicación es obligatoria.");
            if (line.Quantity <= 0 || decimal.Round(line.Quantity, 3) != line.Quantity || line.Quantity >= 1_000_000_000_000_000m)
                throw StockProblems.Validation($"lines[{index}].quantity",
                    "La cantidad debe ser positiva, con un máximo de tres decimales.");
            if (line.OwnReserved < 0 || line.OwnReserved > line.Quantity || decimal.Round(line.OwnReserved, 3) != line.OwnReserved)
                throw StockProblems.Validation($"lines[{index}].ownReservedToConsume",
                    "La reserva propia a consumir debe estar entre cero y la cantidad.");
            string? lotCode;
            try { lotCode = Lot.NormalizeCode(line.LotCode); }
            catch (ArgumentException error)
            { throw StockProblems.Validation($"lines[{index}].lotCode", error.Message); }
            normalized.Add(line with { LotCode = lotCode });
        }

        // Un bucket aparece una sola vez por comando: evita aplicar validaciones sobre saldos parciales.
        if (normalized.GroupBy(line => (line.ArticleId, line.LocationId, line.LotCode ?? Lot.UntrackedCode))
            .Any(group => group.Count() > 1))
            throw StockProblems.Validation("lines", "Cada artículo, ubicación y lote debe aparecer una sola vez.");

        var bodyObject = JsonSerializer.SerializeToNode(new
        {
            operation = operation.ToString(),
            sourceType,
            sourceId = context.Source.Id,
            occurredAt = occurredAt.ToString("O", CultureInfo.InvariantCulture),
            reason,
            supportReference = support,
            supportNotApplicableReason = notApplicable,
            counterpartyReference = counterparty,
            lines = normalized.Select(line => new
            {
                articleId = line.ArticleId,
                locationId = line.LocationId,
                lotCode = line.LotCode,
                quantity = Format(line.Quantity),
                ownReservedToConsume = Format(line.OwnReserved)
            })
        }, OperationResponse.JsonOptions)!.AsObject();
        // Omitir el campo nuevo cuando es null conserva las huellas anteriores a esta ampliación.
        if (counterpartyNotApplicable is not null) bodyObject["counterpartyNotApplicableReason"] = counterpartyNotApplicable;
        var body = JsonSerializer.SerializeToElement(bodyObject, OperationResponse.JsonOptions);

        return new StockCommand(operation, sourceType, context.Source.Id, occurredAt, reason, support, notApplicable,
            counterparty, counterpartyNotApplicable, normalized, body);
    }

    private static string? Optional(string? value, int maximumLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw StockProblems.Validation(field, $"No puede superar {maximumLength} caracteres.");
        return normalized;
    }

    private static string Format(decimal value) => value.ToString("0.000", CultureInfo.InvariantCulture);

    [GeneratedRegex("^[a-z][a-z0-9-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex DocumentType();

    private sealed record ResolvedLine(Article Article, Location Location, string LotCode, decimal Quantity, decimal OwnReserved);
}

internal sealed record StockLine(Guid ArticleId, Guid LocationId, string? LotCode, decimal Quantity, decimal OwnReserved);

// Comando validado y normalizado. NormalizedBody va en OperationRequest: el ejecutor de A
// calcula con él la huella de idempotencia.
public sealed class StockCommand
{
    internal StockCommand(StockOperation operation, string sourceType, Guid sourceId, DateTimeOffset occurredAt,
        string reason, string? supportReference, string? supportNotApplicableReason, string? counterpartyReference,
        string? counterpartyNotApplicableReason,
        IReadOnlyList<StockLine> lines, JsonElement normalizedBody)
    {
        Operation = operation;
        SourceType = sourceType;
        SourceId = sourceId;
        OccurredAt = occurredAt;
        Reason = reason;
        SupportReference = supportReference;
        SupportNotApplicableReason = supportNotApplicableReason;
        CounterpartyReference = counterpartyReference;
        CounterpartyNotApplicableReason = counterpartyNotApplicableReason;
        Lines = lines;
        NormalizedBody = normalizedBody;
    }

    public StockOperation Operation { get; }
    public string SourceType { get; }
    public Guid SourceId { get; }
    public DateTimeOffset OccurredAt { get; }
    public string Reason { get; }
    public string? SupportReference { get; }
    public string? SupportNotApplicableReason { get; }
    public string? CounterpartyReference { get; }
    public string? CounterpartyNotApplicableReason { get; }
    internal IReadOnlyList<StockLine> Lines { get; }
    public JsonElement NormalizedBody { get; }
}

// Rechazos del núcleo con el patrón de A (InventoryProblemException → MasterDataProblemMapper →
// ProblemDetails). Los códigos y estados son los del contrato HTTP común.
internal static class StockProblems
{
    public static InventoryProblemException Validation(string field, string message) =>
        InventoryProblemException.Validation(field, message);

    public static InventoryProblemException InsufficientStock(string message) => new(409, "InsufficientStock", message);

    public static InventoryProblemException ReservedConflict(string message) => new(409, "ReservedStockConflict", message);

    public static InventoryProblemException ArticleInactive(string message) => new(409, "ArticleInactive", message);

    public static InventoryProblemException AlreadyConfirmed(string message) => new(409, "DocumentAlreadyConfirmed", message);
}

internal static class StockOperationExtensions
{
    public static MovementKind ToMovementKind(this StockOperation operation) => operation switch
    {
        StockOperation.Receive => MovementKind.Receipt,
        StockOperation.Reserve => MovementKind.Reservation,
        StockOperation.Release => MovementKind.Release,
        StockOperation.Ship => MovementKind.Shipment,
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };
}
