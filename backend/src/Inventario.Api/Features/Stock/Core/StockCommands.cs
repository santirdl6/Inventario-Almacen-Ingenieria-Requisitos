namespace Inventario.Api.Features.Stock.Core;

// Documento origen de un hecho de stock. Cada hecho (confirmación, reserva, liberación)
// tiene su propio identificador; el diario impide aplicarlo dos veces.
public sealed record StockDocumentRef(string Type, Guid Id);

// Datos comunes de cualquier comando. Actor y RecordedAt los asigna el servidor.
public sealed record StockCommandContext(
    Guid OperationId,
    StockDocumentRef Source,
    DateTimeOffset OccurredAt,
    string Reason,
    string? SupportReference = null,
    string? SupportNotApplicableReason = null,
    string? CounterpartyReference = null);

// Asignación explícita a un bucket. LotCode omitido equivale a SIN-LOTE en artículos sin seguimiento.
public sealed record StockAllocation(Guid ArticleId, Guid LocationId, string? LotCode, decimal Quantity);

// Para una salida, OwnReservedToConsume es la reserva remanente del propio pedido en ese bucket
// que se consume primero (Consumir = min(Q, Rpropia), calculado por HU-03 a partir de sus reservas).
public sealed record ShipmentAllocation(Guid ArticleId, Guid LocationId, string? LotCode,
    decimal Quantity, decimal OwnReservedToConsume);

public sealed record StockOperationOutcome(
    Guid OperationId,
    Guid MovementId,
    string SourceDocumentType,
    Guid SourceDocumentId,
    DateTimeOffset RecordedAt)
{
    // true cuando la respuesta proviene de un OperationResult ya confirmado (reintento).
    public bool Replayed { get; init; }
}
