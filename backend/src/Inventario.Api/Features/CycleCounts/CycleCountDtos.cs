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

public sealed record CountLineView(Guid Id, Guid ArticleId, Guid LocationId, string LotCode, bool IsFound, string TheoreticalPhysical,
    string? CountedPhysical, string? Difference, Guid? CountedBy, DateTimeOffset? CountedAt, string? Reason, Guid? ReasonBy,
    Guid? CorrectsShipmentId)
{
    internal static CountLineView From(CountLine line) => new(line.Id, line.ArticleId, line.LocationId, line.LotCode, line.IsFound,
        CycleCountView.Quantity(line.TheoreticalPhysical), CycleCountView.Quantity(line.CountedPhysical),
        CycleCountView.Quantity(line.CountedPhysical is { } counted ? counted - line.TheoreticalPhysical : null),
        line.CountedBy, line.CountedAt, line.Reason, line.ReasonBy, line.CorrectsShipmentId);
}

/// <summary>Respuesta de las escrituras que devuelven el conteo con su nuevo ETag.</summary>
public sealed record CycleCountMutation(Guid OperationId, CycleCountView Count);

/// <summary>Fila de GET /cycle-counts.</summary>
public sealed record CycleCountSummary(Guid Id, string CountCode, string State, IReadOnlyList<Guid> LocationIds, Guid CreatedBy,
    DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? ClosedAt);

/// <summary>GET /cycle-counts/{id}/context: datos actuales sin ETag, con countVersion (D-12).</summary>
public sealed record CycleCountContext(string CountVersion, IReadOnlyList<CountArticleContext> Articles,
    IReadOnlyList<CountLocationContext> Locations, IReadOnlyList<CountBucketReserved> ReservedByBucket,
    IReadOnlyList<CountShipmentContext> Shipments, IReadOnlyList<CountUserContext> Users);

public sealed record CountArticleContext(Guid Id, string Sku, string Name, string Unit, bool AllowsFractions, bool TracksLot, bool IsActive);

public sealed record CountLocationContext(Guid Id, string Code, bool IsActive);

public sealed record CountBucketReserved(Guid ArticleId, Guid LocationId, string LotCode, string Reserved);

public sealed record CountShipmentContext(Guid ShipmentId, Guid CustomerOrderId, string ExternalReference);

public sealed record CountUserContext(Guid Id, string Name);
