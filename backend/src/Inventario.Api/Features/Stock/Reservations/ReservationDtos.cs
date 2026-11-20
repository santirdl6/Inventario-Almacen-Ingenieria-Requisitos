using System.Globalization;
using System.Text.Json;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.Stock.CustomerOrders;
using Inventario.Api.Infrastructure.Http;

namespace Inventario.Api.Features.Stock.Reservations;

// Contrato de reservas (specs/004-consultar-existencias/contracts/stock-api.md § POST /reservations,
// GET /reservations/{id}, GET /stock/reservations). Cantidades como cadenas con tres decimales.

/// <summary>
/// Cuerpo normalizado de POST /reservations: es la intención que firma la huella idempotente
/// (research D-05). Ids en formato D, lotId null si se omite y cantidades válidas con tres
/// decimales, en el orden recibido. Lo inválido se conserva recortado para señalarlo.
/// </summary>
public sealed record ReservationWrite(string? CustomerOrderLineId, IReadOnlyList<ReservationAllocationWrite>? Allocations)
{
    public static ReservationWrite Read(JsonElement body)
    {
        var properties = JsonBody.Object(body, "customerOrderLineId", "allocations");
        return new(JsonBody.Id(properties, "customerOrderLineId"),
            JsonBody.Array(properties, "allocations", item =>
            {
                var line = JsonBody.Object(item, "locationId", "lotId", "quantity");
                return new ReservationAllocationWrite(JsonBody.Id(line, "locationId"), JsonBody.Id(line, "lotId"),
                    JsonBody.Quantity(line, "quantity"));
            }));
    }
}

public sealed record ReservationAllocationWrite(string? LocationId, string? LotId, string? Quantity);

/// <summary>201 de POST /reservations, sin ETag (D-08): la UI vuelve a leer el pedido.</summary>
public sealed record ReservationResult(Guid OperationId, Guid ReservationId, Guid CustomerOrderId, Guid MovementId);

/// <summary>GET /reservations/{id}: solo datos propios, con ETag de Reservation.Version.</summary>
public sealed record ReservationView(Guid Id, string State, Guid CustomerOrderId, Guid CustomerOrderLineId, Guid ArticleId,
    IReadOnlyList<CustomerOrderAllocationView> Allocations)
{
    public static ReservationView From(Reservation reservation, Guid orderId, Guid articleId) =>
        new(reservation.Id, reservation.State.ToString(), orderId, reservation.CustomerOrderLineId, articleId,
            reservation.Allocations.OrderBy(allocation => allocation.Id).Select(allocation =>
                new CustomerOrderAllocationView(allocation.Id, allocation.ArticleId, allocation.LocationId, allocation.LotId,
                    CustomerOrderView.Quantity(allocation.AllocatedQuantity), CustomerOrderView.Quantity(allocation.ConsumedQuantity),
                    CustomerOrderView.Quantity(allocation.ReleasedQuantity), CustomerOrderView.Quantity(allocation.RemainingQuantity)))
                .ToList());
}

/// <summary>GET /stock/reservations: una fila por asignación con remanente, y el total del filtro.</summary>
public sealed record ReservationCommitments(DateTimeOffset AsOf, CommitmentTotals Totals, IReadOnlyList<CommitmentRow> Items,
    int Page, int PageSize, int TotalCount);

public sealed record CommitmentTotals(string Remaining);

public sealed record CommitmentRow(CommitmentOrder CustomerOrder, Guid CustomerOrderLineId, Guid ReservationId,
    Guid AllocationId, string ReservationState, CommitmentLocation Location, CommitmentLot Lot,
    string Allocated, string Consumed, string Released, string Remaining);

public sealed record CommitmentOrder(Guid Id, string ExternalReference, string State);

public sealed record CommitmentLocation(Guid Id, string Code);

public sealed record CommitmentLot(Guid Id, string Code, bool IsUntracked);

/// <summary>Lectura tolerante de cuerpos JSON de HU-05: tipos erróneos → 400; campos ajenos → 422.</summary>
internal static class JsonBody
{
    public static Dictionary<string, JsonElement> Object(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InventoryProblemException(400, "InvalidRequest", "Se requiere un objeto JSON.");
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
                throw InventoryProblemException.Validation(property.Name, "Este campo no forma parte de la operación.");
            properties[property.Name] = property.Value;
        }
        return properties;
    }

    public static string? Text(Dictionary<string, JsonElement> properties, string name) =>
        !properties.TryGetValue(name, out var value) ? null : value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString()!.Trim(),
            _ => throw InvalidTypes()
        };

    public static string? Id(Dictionary<string, JsonElement> properties, string name)
    {
        var text = Text(properties, name);
        return Guid.TryParse(text, out var id) ? id.ToString("D") : text;
    }

    public static string? Quantity(Dictionary<string, JsonElement> properties, string name)
    {
        var text = properties.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetRawText() : Text(properties, name);
        return CustomerOrderWrite.TryQuantity(text, out var quantity) && decimal.Round(quantity, 3) == quantity
            ? quantity.ToString("0.000", CultureInfo.InvariantCulture) : text;
    }

    public static List<T>? Array<T>(Dictionary<string, JsonElement> properties, string name, Func<JsonElement, T> read)
    {
        if (!properties.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Array) throw InvalidTypes();
        return value.EnumerateArray().Select(read).ToList();
    }

    /// <summary>Cantidad positiva con tres decimales como máximo; null si es válida.</summary>
    public static string? QuantityProblem(string? text, out decimal quantity) =>
        !CustomerOrderWrite.TryQuantity(text, out quantity) ? "Indica una cantidad numérica."
        : quantity <= 0 ? "La cantidad debe ser mayor que cero."
        : decimal.Round(quantity, 3) != quantity ? "La cantidad admite como máximo tres decimales."
        : quantity >= 1_000_000_000_000_000m ? "La cantidad es demasiado grande."
        : null;

    private static InventoryProblemException InvalidTypes() =>
        new(400, "InvalidRequest", "Los tipos JSON de la operación no son válidos.");
}
