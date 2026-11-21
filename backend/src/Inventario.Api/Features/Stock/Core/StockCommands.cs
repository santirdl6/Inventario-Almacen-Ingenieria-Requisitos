namespace Inventario.Api.Features.Stock.Core;

public enum StockOperation
{
    Receive,
    Reserve,
    Release,
    Ship,
    // HU-04 (D-13): ajuste por conteo; cada línea lleva la diferencia con signo.
    Adjust
}

// Documento origen de un hecho de stock. Cada hecho (confirmación, reserva, liberación)
// tiene su propio identificador; el diario impide aplicarlo dos veces. Type es canónico
// (minúsculas, p. ej. "receipt"), el mismo que usa el bloqueo document:{type}:{id}.
public sealed record StockDocumentRef(string Type, Guid Id);

// Datos comunes de cualquier comando. OperationId, actor y RecordedAt los aporta el
// ejecutor común de A (OperationContext) o el servidor; nunca el cliente.
public sealed record StockCommandContext(
    StockDocumentRef Source,
    DateTimeOffset OccurredAt,
    string Reason,
    string? SupportReference = null,
    string? SupportNotApplicableReason = null,
    string? CounterpartyReference = null,
    string? CounterpartyNotApplicableReason = null);

// Asignación explícita a un bucket. LotCode omitido equivale a SIN-LOTE en artículos sin seguimiento.
public sealed record StockAllocation(Guid ArticleId, Guid LocationId, string? LotCode, decimal Quantity);

// Para una salida, OwnReservedToConsume es la reserva remanente del propio pedido en ese bucket
// que se consume primero (Consumir = min(Q, Rpropia), calculado por HU-03 a partir de sus reservas).
public sealed record ShipmentAllocation(Guid ArticleId, Guid LocationId, string? LotCode,
    decimal Quantity, decimal OwnReservedToConsume);

// HU-04 (D-13): diferencia con signo de un bucket (contado − teórico); nunca cero.
public sealed record AdjustmentAllocation(Guid ArticleId, Guid LocationId, string? LotCode, decimal Difference);

public sealed record StockOperationOutcome(
    Guid OperationId,
    Guid MovementId,
    string SourceDocumentType,
    Guid SourceDocumentId,
    DateTimeOffset RecordedAt);
