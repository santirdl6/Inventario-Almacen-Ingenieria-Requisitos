using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence.Operations;

namespace Inventario.Api.Features.Locations;

// Transporte HTTP de HU-02 (specs/003-gestionar-ubicaciones/contracts/locations-api.md).
// Las entidades EF no se exponen; las cantidades viajan como cadenas con tres decimales.

/// <summary>Cuerpo de POST y PUT. Solo normaliza: las reglas se validan dentro del ejecutor,
/// después de resolver un posible replay (como FamilyWrite de A).</summary>
public sealed record LocationWrite([property: Required, StringLength(Location.CodeMaxLength)] string? Code,
    [property: Required, StringLength(Location.PartMaxLength)] string? Aisle,
    [property: Required, StringLength(Location.PartMaxLength)] string? Rack,
    [property: Required, StringLength(Location.PartMaxLength)] string? Level,
    [property: Required] string? Kind, bool? IsActive)
{
    private static readonly HashSet<string> Fields = ["code", "aisle", "rack", "level", "kind", "isActive"];

    public static LocationWrite Read(JsonElement body, bool create)
    {
        if (body.ValueKind != JsonValueKind.Object)
            throw new InventoryProblemException(400, "InvalidRequest", "Se requiere un objeto JSON.");
        foreach (var property in body.EnumerateObject())
            if (!Fields.Contains(property.Name))
                throw InventoryProblemException.Validation(property.Name, "Este campo no es editable en una ubicación.");
        LocationWrite value;
        try { value = body.Deserialize<LocationWrite>(OperationResponse.JsonOptions)!; }
        catch (JsonException) { throw new InventoryProblemException(400, "InvalidRequest", "Los tipos de los campos JSON no son válidos."); }
        if (!create && value.IsActive is null || body.TryGetProperty("isActive", out var state) && state.ValueKind == JsonValueKind.Null)
            throw InventoryProblemException.Validation("isActive", "El estado activo debe estar definido.");
        var kind = value.Kind?.Trim();
        kind = Enum.TryParse<LocationKind>(kind, true, out var parsed) && Enum.IsDefined(parsed) && !int.TryParse(kind, out _)
            ? parsed.ToString() : kind;
        return new(Upper(value.Code), Upper(value.Aisle), Upper(value.Rack), Upper(value.Level), kind, value.IsActive ?? true);
    }

    // Solo se pasa a mayúsculas un texto que ya cumple la regla ASCII: ToUpperInvariant
    // convertiría caracteres no admitidos («ı» → «I») antes de que la validación los rechace.
    private static string? Upper(string? text)
    {
        var trimmed = text?.Trim();
        return trimmed is not null && Location.HasOnlyAllowedCharacters(trimmed) ? trimmed.ToUpperInvariant() : trimmed;
    }
}

public sealed record LocationSummary(Guid Id, string Code, string Aisle, string Rack, string Level,
    string Description, string Kind, string KindLabel, bool IsActive, string StatusLabel)
{
    public static LocationSummary From(Location location) => new(location.Id, location.Code, location.Aisle,
        location.Rack, location.Level, LocationText.Description(location.Aisle, location.Rack, location.Level),
        location.Kind.ToString(), LocationText.KindLabel(location.Kind), location.IsActive,
        LocationText.StatusLabel(location.IsActive));
}

public sealed record LocationDetail(Guid Id, string Code, string Aisle, string Rack, string Level,
    string Description, string Kind, string KindLabel, bool IsActive, string StatusLabel,
    bool HasMovements, bool IsEmpty, bool HasReservations, bool InOpenCount, bool IsLastActiveReceiving)
{
    public static LocationDetail From(Location location, bool hasMovements, LocationStockTotals totals,
        bool inOpenCount, bool isLastActiveReceiving)
    {
        var summary = LocationSummary.From(location);
        return new(summary.Id, summary.Code, summary.Aisle, summary.Rack, summary.Level, summary.Description,
            summary.Kind, summary.KindLabel, summary.IsActive, summary.StatusLabel, hasMovements, totals.IsEmpty,
            totals.HasReservations, inOpenCount, isLastActiveReceiving);
    }
}

/// <summary>Respuesta de POST y PUT, con la misma forma que las mutaciones de maestros de A.</summary>
public sealed record LocationMutation(Guid OperationId, LocationDetail Resource);

public sealed record LocationRef(Guid Id, string Code, string Description, string Kind, bool IsActive);

public sealed record ArticleRef(Guid Id, string Sku, string Name, string Unit, bool IsActive);

public sealed record QuantityTotals(string Physical, string Reserved);

public sealed record ArticleLocationBucket(LocationRef Location, string LotCode, string Physical, string Reserved);

public sealed record ArticleLocations(DateTimeOffset AsOf, ArticleRef Article, QuantityTotals Totals,
    IReadOnlyList<ArticleLocationBucket> Items, int Page, int PageSize, int TotalCount);

public sealed record LocationStockLine(ArticleRef Article, string LotCode, string Physical, string Reserved, string Unit);

public sealed record LocationStock(DateTimeOffset AsOf, LocationRef Location, IReadOnlyList<LocationStockLine> Items,
    bool HasReservations, bool InOpenCount, int Page, int PageSize, int TotalCount);

public static class LocationText
{
    public const string UntrackedLotLabel = "Sin lote";

    public static string Description(string aisle, string rack, string level) =>
        $"Pasillo {aisle} · Estantería {rack} · Nivel {level}";

    public static string KindLabel(LocationKind kind) => kind switch
    {
        LocationKind.Storage => "Almacenamiento",
        LocationKind.Receiving => "Zona de recepción",
        _ => kind.ToString()
    };

    public static string StatusLabel(bool isActive) => isActive ? "Activa" : "Inactiva";

    public static string Quantity(decimal value) => value.ToString("0.000", CultureInfo.InvariantCulture);

    public static LocationRef Ref(Location location) => new(location.Id, location.Code,
        Description(location.Aisle, location.Rack, location.Level), location.Kind.ToString(), location.IsActive);
}

// Paginación del contrato: page desde 1, pageSize 25 por defecto y 100 como máximo; fuera de
// rango se rechaza con 422 y no se recorta (contracts/locations-api.md § Paginación).
public readonly record struct Paging(int Page, int PageSize)
{
    public const int DefaultPageSize = 25;
    public const int MaximumPageSize = 100;

    public int Skip => (Page - 1) * PageSize;

    public static Paging From(int? page, int? pageSize)
    {
        if (page is < 1)
            throw InventoryProblemException.Validation("page", "La página empieza en 1.");
        if (pageSize is < 1 or > MaximumPageSize)
            throw InventoryProblemException.Validation("pageSize", $"El tamaño de página debe estar entre 1 y {MaximumPageSize}.");
        if (((long)(page ?? 1) - 1) * (pageSize ?? DefaultPageSize) > int.MaxValue)
            throw InventoryProblemException.Validation("page", "La página solicitada está fuera de rango.");
        return new Paging(page ?? 1, pageSize ?? DefaultPageSize);
    }
}
