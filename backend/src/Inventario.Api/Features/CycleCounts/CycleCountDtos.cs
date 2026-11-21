using System.Globalization;
using System.Text.Json;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence.Operations;

namespace Inventario.Api.Features.CycleCounts;

// Contrato de conteos (specs/006-realizar-inventarios-ciclicos/contracts/cycle-counts-api.md). Las
// cantidades viajan como cadenas con tres decimales, como en HU-05 y HU-03.

/// <summary>
/// Cuerpo de POST y PUT /cycle-counts. Conserva el orden recibido para señalar errores por índice; la
/// huella idempotente usa las ubicaciones ordenadas, porque el alcance es un conjunto (contrato § Escrituras).
/// </summary>
public sealed record CycleCountWrite(IReadOnlyList<string?>? LocationIds)
{
    public static CycleCountWrite Read(JsonElement body)
    {
        var root = CycleCountJson.Object(body);
        List<string?>? locations = null;
        foreach (var property in root.EnumerateObject())
            switch (property.Name)
            {
                case "locationIds": locations = CycleCountJson.Ids(property.Value); break;
                default: throw InventoryProblemException.Validation(property.Name, "Este campo no forma parte del conteo.");
            }
        return new(locations);
    }

    public JsonElement Fingerprint() => JsonSerializer.SerializeToElement(
        new { locationIds = (LocationIds ?? []).OrderBy(value => value, StringComparer.Ordinal).ToList() }, OperationResponse.JsonOptions);
}

/// <summary>
/// Cuerpo de PUT /cycle-counts/{id}/lines (T008): contado de líneas existentes y hallazgos. Sin motivo:
/// el motivo de cada diferencia se fija solo en la revisión (H6 de analyze-1), así que «reason» es un
/// campo desconocido. Conserva el orden para los errores; la huella ordena líneas y hallazgos.
/// </summary>
public sealed record CycleCountLinesWrite(IReadOnlyList<CountedWrite>? Counted, IReadOnlyList<FoundWrite>? Found)
{
    public static CycleCountLinesWrite Read(JsonElement body)
    {
        var root = CycleCountJson.Object(body);
        List<CountedWrite>? counted = null;
        List<FoundWrite>? found = null;
        foreach (var property in root.EnumerateObject())
            switch (property.Name)
            {
                case "counted": counted = CycleCountJson.Items(property.Value, ReadCounted); break;
                case "found": found = CycleCountJson.Items(property.Value, ReadFound); break;
                default: throw InventoryProblemException.Validation(property.Name, "Este campo no forma parte del contado.");
            }
        return new(counted, found);
    }

    private static CountedWrite ReadCounted(JsonElement item)
    {
        string? line = null, quantity = null;
        foreach (var property in item.EnumerateObject())
            switch (property.Name)
            {
                case "lineId": line = CycleCountJson.NormalizeId(CycleCountJson.Text(property.Value)); break;
                case "countedPhysical": quantity = CycleCountJson.Quantity(property.Value); break;
                default: throw InventoryProblemException.Validation(property.Name, "Este campo no forma parte de la línea contada.");
            }
        return new(line, quantity);
    }

    private static FoundWrite ReadFound(JsonElement item)
    {
        string? article = null, location = null, lot = null, quantity = null;
        foreach (var property in item.EnumerateObject())
            switch (property.Name)
            {
                case "articleId": article = CycleCountJson.NormalizeId(CycleCountJson.Text(property.Value)); break;
                case "locationId": location = CycleCountJson.NormalizeId(CycleCountJson.Text(property.Value)); break;
                case "lotCode":
                    var text = CycleCountJson.Text(property.Value);
                    lot = string.IsNullOrEmpty(text) ? null : text.ToUpperInvariant();
                    break;
                case "countedPhysical": quantity = CycleCountJson.Quantity(property.Value); break;
                default: throw InventoryProblemException.Validation(property.Name, "Este campo no forma parte del hallazgo.");
            }
        return new(article, location, lot, quantity);
    }

    public JsonElement Fingerprint() => JsonSerializer.SerializeToElement(new
    {
        counted = (Counted ?? []).OrderBy(line => line.LineId, StringComparer.Ordinal).ToList(),
        found = (Found ?? []).OrderBy(line => line.ArticleId, StringComparer.Ordinal).ThenBy(line => line.LocationId, StringComparer.Ordinal)
            .ThenBy(line => line.LotCode, StringComparer.Ordinal).ToList()
    }, OperationResponse.JsonOptions);
}

public sealed record CountedWrite(string? LineId, string? CountedPhysical);

public sealed record FoundWrite(string? ArticleId, string? LocationId, string? LotCode, string? CountedPhysical);

/// <summary>
/// Cuerpo de PUT /cycle-counts/{id}/review (T009, T012): motivo por línea (vacío lo retira) y, opcional,
/// la salida confirmada que la diferencia corrige (D-09); ausente o null retira el vínculo.
/// </summary>
public sealed record CycleCountReviewWrite(IReadOnlyList<ReviewLineWrite>? Lines)
{
    public static CycleCountReviewWrite Read(JsonElement body)
    {
        var root = CycleCountJson.Object(body);
        List<ReviewLineWrite>? lines = null;
        foreach (var property in root.EnumerateObject())
            switch (property.Name)
            {
                case "lines": lines = CycleCountJson.Items(property.Value, ReadLine); break;
                default: throw InventoryProblemException.Validation(property.Name, "Este campo no forma parte de la revisión.");
            }
        return new(lines);
    }

    private static ReviewLineWrite ReadLine(JsonElement item)
    {
        string? line = null, reason = null, shipment = null;
        foreach (var property in item.EnumerateObject())
            switch (property.Name)
            {
                case "lineId": line = CycleCountJson.NormalizeId(CycleCountJson.Text(property.Value)); break;
                case "reason":
                    var text = CycleCountJson.Text(property.Value);
                    reason = string.IsNullOrEmpty(text) ? null : text;
                    break;
                case "correctsShipmentId":
                    var id = CycleCountJson.Text(property.Value);
                    shipment = string.IsNullOrEmpty(id) ? null : CycleCountJson.NormalizeId(id);
                    break;
                default: throw InventoryProblemException.Validation(property.Name, "Este campo no forma parte de la línea revisada.");
            }
        return new(line, reason, shipment);
    }

    public JsonElement Fingerprint() => JsonSerializer.SerializeToElement(
        new { lines = (Lines ?? []).OrderBy(line => line.LineId, StringComparer.Ordinal).ToList() }, OperationResponse.JsonOptions);
}

public sealed record ReviewLineWrite(string? LineId, string? Reason, string? CorrectsShipmentId = null);

/// <summary>Cuerpo de POST /cycle-counts/{id}/cancel: motivo de 1 a 500 caracteres.</summary>
public sealed record CycleCountCancelWrite(string? Reason)
{
    public static CycleCountCancelWrite Read(JsonElement body)
    {
        var root = CycleCountJson.Object(body);
        string? reason = null;
        foreach (var property in root.EnumerateObject())
            switch (property.Name)
            {
                case "reason": reason = CycleCountJson.Text(property.Value); break;
                default: throw InventoryProblemException.Validation(property.Name, "Este campo no forma parte de la cancelación.");
            }
        return new(string.IsNullOrEmpty(reason) ? null : reason);
    }
}

/// <summary>Respuesta de POST /cycle-counts/{id}/confirm (sin ETag): el movimiento solo si hubo diferencias.</summary>
public sealed record CycleCountConfirmation(Guid OperationId, Guid CountId, Guid? MovementId, IReadOnlyList<AdjustedLine> AdjustedLines,
    IReadOnlyList<ShipmentCorrection> ShipmentCorrections);

public sealed record AdjustedLine(Guid LineId, string Difference);

/// <summary>Respuesta de POST /cycle-counts/{id}/cancel (sin ETag).</summary>
public sealed record CycleCountCancellation(Guid OperationId, Guid CountId);

/// <summary>Lectura estricta de JSON compartida por los cuerpos de conteos.</summary>
internal static class CycleCountJson
{
    public static JsonElement Object(JsonElement body) =>
        body.ValueKind == JsonValueKind.Object ? body : throw new InventoryProblemException(400, "InvalidRequest", "Se requiere un objeto JSON.");

    public static List<string?>? Ids(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Array) throw InvalidTypes();
        return value.EnumerateArray().Select(item => NormalizeId(Text(item))).ToList();
    }

    public static List<T>? Items<T>(JsonElement value, Func<JsonElement, T> read)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Array) throw InvalidTypes();
        return value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.Object ? read(item) : throw InvalidTypes()).ToList();
    }

    public static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString()!.Trim(),
        _ => throw InvalidTypes()
    };

    /// <summary>Cantidad como texto: número JSON o cadena, normalizada a tres decimales si es válida.</summary>
    public static string? Quantity(JsonElement value)
    {
        var text = value.ValueKind == JsonValueKind.Number ? value.GetRawText() : Text(value);
        return TryQuantity(text, out var quantity) && decimal.Round(quantity, 3) == quantity
            ? quantity.ToString("0.000", CultureInfo.InvariantCulture) : text;
    }

    public static string? NormalizeId(string? value) => Guid.TryParse(value, out var id) ? id.ToString("D") : value;

    public static bool TryQuantity(string? value, out decimal quantity) =>
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture, out quantity);

    public static InventoryProblemException InvalidTypes() => new(400, "InvalidRequest", "Los tipos JSON del conteo no son válidos.");
}

/// <summary>GET /cycle-counts/{id}: solo datos propios, con ETag de CycleCount.Version (D-12).</summary>
public sealed record CycleCountView(Guid Id, string CountCode, string State, Guid CreatedBy, DateTimeOffset CreatedAt,
    Guid? StartedBy, DateTimeOffset? StartedAt, Guid? SubmittedBy, DateTimeOffset? SubmittedAt, Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt, Guid? CancelledBy, DateTimeOffset? CancelledAt, string? CancelReason, Guid? OperationId,
    Guid? AdjustmentMovementId, IReadOnlyList<CountLocationView> Locations, IReadOnlyList<CountLineView> Lines)
{
    internal static CycleCountView From(CycleCount count) => new(count.Id, count.CountCode, count.State.ToString(), count.CreatedBy,
        count.CreatedAt, count.StartedBy, count.StartedAt, count.SubmittedBy, count.SubmittedAt, count.ApprovedBy, count.ApprovedAt,
        count.CancelledBy, count.CancelledAt, count.CancelReason, count.OperationId, count.AdjustmentMovementId,
        count.Locations.OrderBy(location => location.LocationId).Select(location => new CountLocationView(location.LocationId,
            location.IsFrozen)).ToList(),
        count.Lines.OrderBy(line => line.Id).Select(CountLineView.From).ToList());

    public static string Quantity(decimal value) => value.ToString("0.000", CultureInfo.InvariantCulture);

    public static string? Quantity(decimal? value) => value is { } number ? Quantity(number) : null;
}

public sealed record CountLocationView(Guid LocationId, bool IsFrozen);

/// <summary>
/// Línea del detalle. Con <see cref="CorrectsShipmentId"/>, <see cref="ShipmentCorrectionNotice"/> deja explícita
/// la limitación P-07 en la respuesta: el ajuste no modifica lo servido ni el estado del pedido.
/// </summary>
public sealed record CountLineView(Guid Id, Guid ArticleId, Guid LocationId, string LotCode, bool IsFound, string TheoreticalPhysical,
    string? CountedPhysical, string? Difference, Guid? CountedBy, DateTimeOffset? CountedAt, string? Reason, Guid? ReasonBy,
    Guid? CorrectsShipmentId, string? ShipmentCorrectionNotice)
{
    internal static CountLineView From(CountLine line) => new(line.Id, line.ArticleId, line.LocationId, line.LotCode, line.IsFound,
        CycleCountView.Quantity(line.TheoreticalPhysical), CycleCountView.Quantity(line.CountedPhysical),
        CycleCountView.Quantity(line.CountedPhysical is { } counted ? counted - line.TheoreticalPhysical : null),
        line.CountedBy, line.CountedAt, line.Reason, line.ReasonBy, line.CorrectsShipmentId,
        line.CorrectsShipmentId is null ? null : ShipmentCorrections.Notice);
}

/// <summary>
/// Corrección de una salida confirmada mediante el conteo (D-09). Limitación P-07, decisión de alcance
/// aprobada por A el 2026-11-21: el ajuste devuelve la diferencia al saldo de la ubicación, pero la salida,
/// lo servido del pedido, su estado y los consumos de reservas no cambian (FR-019). Corregir el pedido
/// exigiría su acumulado servido y su estado en el mismo commit, según el contrato común de correcciones.
/// </summary>
public static class ShipmentCorrections
{
    public const string Notice =
        "El ajuste corrige solo las existencias: no cambia lo servido del pedido, su estado ni las reservas consumidas (P-07).";
}

/// <summary>Corrección aplicada al confirmar: OrderChanged es siempre false por la limitación P-07.</summary>
public sealed record ShipmentCorrection(Guid LineId, Guid ShipmentId, Guid CustomerOrderId, bool OrderChanged, string Notice);

/// <summary>Respuesta de las escrituras que devuelven el conteo con su nuevo ETag.</summary>
public sealed record CycleCountMutation(Guid OperationId, CycleCountView Count);

/// <summary>Fila de GET /cycle-counts, con responsables y códigos del alcance (FR-023, Convergence C2).</summary>
public sealed record CycleCountSummary(Guid Id, string CountCode, string State, IReadOnlyList<Guid> LocationIds,
    IReadOnlyList<string> LocationCodes, Guid CreatedBy, string CreatedByName, DateTimeOffset CreatedAt, Guid? StartedBy,
    string? StartedByName, DateTimeOffset? StartedAt, Guid? ClosedBy, string? ClosedByName, DateTimeOffset? ClosedAt);

/// <summary>GET /cycle-counts/{id}/context: datos actuales sin ETag, con countVersion (D-12).</summary>
public sealed record CycleCountContext(string CountVersion, IReadOnlyList<CountArticleContext> Articles,
    IReadOnlyList<CountLocationContext> Locations, IReadOnlyList<CountBucketReserved> ReservedByBucket,
    IReadOnlyList<CountShipmentContext> Shipments, IReadOnlyList<CountUserContext> Users);

public sealed record CountArticleContext(Guid Id, string Sku, string Name, string Unit, bool AllowsFractions, bool TracksLot, bool IsActive);

public sealed record CountLocationContext(Guid Id, string Code, bool IsActive);

public sealed record CountBucketReserved(Guid ArticleId, Guid LocationId, string LotCode, string Reserved);

public sealed record CountShipmentContext(Guid ShipmentId, Guid CustomerOrderId, string ExternalReference);

public sealed record CountUserContext(Guid Id, string Name);

/// <summary>GET /cycle-counts/{id}/lines/{lineId}/shipment-candidates: salida confirmada que tocó el bucket.</summary>
public sealed record ShipmentCandidate(Guid ShipmentId, Guid CustomerOrderId, string ExternalReference, DateTimeOffset ConfirmedAt,
    string Quantity, IReadOnlyList<Guid> LinkedCountLineIds);
