using System.Globalization;
using System.Text.Json;
using Inventario.Api.Domain.Shipping;
using Inventario.Api.Infrastructure.Http;

namespace Inventario.Api.Features.Shipping;

// Contrato de salidas (specs/005-registrar-salida-mercancia/contracts/shipping-api.md). Las
// cantidades viajan como cadenas con tres decimales, como en HU-05.

/// <summary>
/// Cuerpo normalizado de POST y PUT /shipments: es la intención que firma la huella idempotente
/// (D-02). Identificadores en formato D, cantidades válidas con tres decimales y la fecha del hecho
/// en UTC; lo que no es válido se conserva recortado para que la validación lo señale por campo.
/// </summary>
public sealed record ShipmentWrite(string? CustomerOrderId, string? OccurredAt, IReadOnlyList<ShipmentLineWrite>? Lines)
{
    public static ShipmentWrite Read(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
            throw new InventoryProblemException(400, "InvalidRequest", "Se requiere un objeto JSON.");
        string? order = null, occurredAt = null;
        List<ShipmentLineWrite>? lines = null;
        foreach (var property in body.EnumerateObject())
            switch (property.Name)
            {
                case "customerOrderId": order = NormalizeId(Text(property.Value)); break;
                case "occurredAt": occurredAt = NormalizeInstant(Text(property.Value)); break;
                case "lines": lines = ReadLines(property.Value); break;
                default: throw InventoryProblemException.Validation(property.Name, "Este campo no forma parte de la salida.");
            }
        return new(order, occurredAt, lines);
    }

    private static List<ShipmentLineWrite>? ReadLines(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Array) throw InvalidTypes();
        var lines = new List<ShipmentLineWrite>();
        foreach (var line in value.EnumerateArray())
        {
            if (line.ValueKind != JsonValueKind.Object) throw InvalidTypes();
            string? orderLine = null, location = null, lot = null, quantity = null;
            foreach (var property in line.EnumerateObject())
                switch (property.Name)
                {
                    case "customerOrderLineId": orderLine = Text(property.Value); break;
                    case "locationId": location = Text(property.Value); break;
                    case "lotId": lot = Text(property.Value); break;
                    case "quantity":
                        quantity = property.Value.ValueKind == JsonValueKind.Number ? property.Value.GetRawText() : Text(property.Value);
                        break;
                    default: throw InventoryProblemException.Validation(property.Name, "Este campo no forma parte de la línea.");
                }
            lines.Add(new(NormalizeId(orderLine), NormalizeId(location), NormalizeId(lot), NormalizeQuantity(quantity)));
        }
        return lines;
    }

    private static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString()!.Trim(),
        _ => throw InvalidTypes()
    };

    private static string? NormalizeId(string? value) => Guid.TryParse(value, out var id) ? id.ToString("D") : value;

    private static string? NormalizeInstant(string? value) =>
        TryInstant(value, out var instant) ? instant.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) : value;

    private static string? NormalizeQuantity(string? value) =>
        TryQuantity(value, out var quantity) && decimal.Round(quantity, 3) == quantity
            ? quantity.ToString("0.000", CultureInfo.InvariantCulture) : value;

    internal static bool TryInstant(string? value, out DateTimeOffset instant) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out instant);

    internal static bool TryQuantity(string? value, out decimal quantity) =>
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture, out quantity);

    private static InventoryProblemException InvalidTypes() =>
        new(400, "InvalidRequest", "Los tipos JSON de la salida no son válidos.");
}

public sealed record ShipmentLineWrite(string? CustomerOrderLineId, string? LocationId, string? LotId, string? Quantity);

/// <summary>GET /shipments/{id}: solo datos propios, con ETag de Shipment.Version (D-12).</summary>
public sealed record ShipmentView(Guid Id, Guid CustomerOrderId, string State, DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt, Guid ActorId, Guid? OperationId, IReadOnlyList<ShipmentLineView> Lines)
{
    internal static ShipmentView From(Shipment shipment, IReadOnlyList<ShipmentConsumptionRow> consumptions)
    {
        var byLine = consumptions.GroupBy(row => row.ShipmentLineId).ToDictionary(group => group.Key, group => group.ToList());
        return new(shipment.Id, shipment.CustomerOrderId, shipment.State.ToString(), shipment.OccurredAt, shipment.RecordedAt,
            shipment.ActorId, shipment.OperationId,
            shipment.Lines.OrderBy(line => line.Id).Select(line =>
            {
                var rows = byLine.TryGetValue(line.Id, out var found) ? found.OrderBy(row => row.Id).ToList() : [];
                return new ShipmentLineView(line.Id, line.CustomerOrderLineId, line.ArticleId, line.LocationId, line.LotId,
                    Quantity(line.Quantity), Quantity(rows.Sum(row => row.Quantity)),
                    rows.Select(row => new ShipmentConsumptionView(row.ReservationAllocationId, row.ReservationId,
                        Quantity(row.Quantity))).ToList());
            }).ToList());
    }

    internal static string Quantity(decimal value) => value.ToString("0.000", CultureInfo.InvariantCulture);
}

/// <summary>Línea con la reserva propia consumida, derivada de los consumos (D-06, D-08).</summary>
public sealed record ShipmentLineView(Guid Id, Guid CustomerOrderLineId, Guid ArticleId, Guid LocationId, Guid LotId,
    string Quantity, string OwnReservationConsumed, IReadOnlyList<ShipmentConsumptionView> Consumptions);

public sealed record ShipmentConsumptionView(Guid ReservationAllocationId, Guid ReservationId, string Quantity);

/// <summary>Consumo leído con la reserva de su asignación (identificadores inmutables).</summary>
internal sealed record ShipmentConsumptionRow(Guid Id, Guid ShipmentLineId, Guid ReservationAllocationId, Guid ReservationId,
    decimal Quantity);

/// <summary>Respuesta de POST y PUT: operación y salida, con ETag en la cabecera.</summary>
public sealed record ShipmentMutation(Guid OperationId, ShipmentView Shipment);

/// <summary>
/// Cuerpo de POST /shipments/{id}/confirm: la versión del pedido que se revisó (D-05). Forma parte de
/// la huella, así que repetir la clave con otra versión es IdempotencyConflict.
/// </summary>
public sealed record ShipmentConfirmWrite(string? OrderVersion)
{
    public static ShipmentConfirmWrite Read(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
            throw new InventoryProblemException(400, "InvalidRequest", "Se requiere un objeto JSON.");
        string? version = null;
        foreach (var property in body.EnumerateObject())
            switch (property.Name)
            {
                case "orderVersion":
                    version = property.Value.ValueKind switch
                    {
                        JsonValueKind.Null => null,
                        JsonValueKind.String => property.Value.GetString()!.Trim(),
                        _ => throw new InventoryProblemException(400, "InvalidRequest", "orderVersion debe ser una cadena.")
                    };
                    break;
                default: throw InventoryProblemException.Validation(property.Name, "Este campo no forma parte de la confirmación.");
            }
        return new(version);
    }
}

/// <summary>Respuesta de POST /shipments/{id}/confirm, sin ETag (el diario se guarda con el resultado).</summary>
public sealed record ShipmentConfirmation(Guid OperationId, Guid ShipmentId, Guid MovementId, Guid CustomerOrderId,
    string OrderState, IReadOnlyList<ShipmentConfirmedLine> Lines);

public sealed record ShipmentConfirmedLine(Guid Id, string Quantity, string OwnReservationConsumed);
