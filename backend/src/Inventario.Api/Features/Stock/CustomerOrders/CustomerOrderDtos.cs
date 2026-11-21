using System.Globalization;
using System.Text.Json;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.Locations;
using Inventario.Api.Infrastructure.Http;

namespace Inventario.Api.Features.Stock.CustomerOrders;

// Contrato de pedidos de cliente (specs/004-consultar-existencias/contracts/stock-api.md). Las
// cantidades viajan como cadenas con tres decimales, igual que en GET /stock y en HU-02.

/// <summary>
/// Cuerpo normalizado de POST /customer-orders: es la intención que firma la huella idempotente
/// (research D-05). Textos recortados, identificadores en formato D y cantidades válidas con tres
/// decimales; lo que no es válido se conserva recortado para que la validación lo señale por línea.
/// </summary>
public sealed record CustomerOrderWrite(string? ExternalReference, string? CustomerReference,
    IReadOnlyList<CustomerOrderLineWrite>? Lines)
{
    public static CustomerOrderWrite Read(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
            throw new InventoryProblemException(400, "InvalidRequest", "Se requiere un objeto JSON.");
        string? external = null, customer = null;
        List<CustomerOrderLineWrite>? lines = null;
        foreach (var property in body.EnumerateObject())
            switch (property.Name)
            {
                case "externalReference": external = Text(property.Value); break;
                case "customerReference": customer = Text(property.Value); break;
                case "lines": lines = ReadLines(property.Value); break;
                default: throw InventoryProblemException.Validation(property.Name, "Este campo no forma parte del pedido.");
            }
        return new(external, customer, lines);
    }

    private static List<CustomerOrderLineWrite>? ReadLines(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Array) throw InvalidTypes();
        var lines = new List<CustomerOrderLineWrite>();
        foreach (var line in value.EnumerateArray())
        {
            if (line.ValueKind != JsonValueKind.Object) throw InvalidTypes();
            string? article = null, quantity = null;
            foreach (var property in line.EnumerateObject())
                switch (property.Name)
                {
                    case "articleId": article = Text(property.Value); break;
                    case "quantity":
                        quantity = property.Value.ValueKind == JsonValueKind.Number ? property.Value.GetRawText() : Text(property.Value);
                        break;
                    default: throw InventoryProblemException.Validation(property.Name, "Este campo no forma parte de la línea.");
                }
            lines.Add(new(NormalizeId(article), NormalizeQuantity(quantity)));
        }
        return lines;
    }

    private static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString()!.Trim(),
        _ => throw InvalidTypes()
    };

    private static string? NormalizeId(string? value) =>
        Guid.TryParse(value, out var id) ? id.ToString("D") : value;

    private static string? NormalizeQuantity(string? value) =>
        TryQuantity(value, out var quantity) && decimal.Round(quantity, 3) == quantity
            ? quantity.ToString("0.000", CultureInfo.InvariantCulture) : value;

    internal static bool TryQuantity(string? value, out decimal quantity) =>
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture, out quantity);

    private static InventoryProblemException InvalidTypes() =>
        new(400, "InvalidRequest", "Los tipos JSON del pedido no son válidos.");
}

public sealed record CustomerOrderLineWrite(string? ArticleId, string? Quantity);

public sealed record CustomerOrderMutation(Guid OperationId, CustomerOrderView Resource);

/// <summary>
/// Detalle versionado de GET /customer-orders/{id}: **solo datos propios** del pedido, sus líneas,
/// reservas y asignaciones (research D-07, D-11). Todos cambian únicamente mediante escritores que
/// renuevan la versión de la cabecera, así que el ETag fuerte identifica la representación entera.
/// Orden estable por Id (líneas, reservas y asignaciones), nunca por SKU, que es de otro agregado.
/// </summary>
public sealed record CustomerOrderView(Guid Id, string ExternalReference, string CustomerReference, string State,
    IReadOnlyList<CustomerOrderLineView> Lines)
{
    public static CustomerOrderView From(CustomerOrder order, IEnumerable<Reservation> reservations)
    {
        var byLine = reservations.ToLookup(reservation => reservation.CustomerOrderLineId);
        return new(order.Id, order.ExternalReference, order.CustomerReference, order.State.ToString(),
            order.Lines.OrderBy(line => line.Id).Select(line =>
            {
                var own = byLine[line.Id].OrderBy(reservation => reservation.Id).ToList();
                var remaining = own.SelectMany(reservation => reservation.Allocations).Sum(allocation => allocation.RemainingQuantity);
                return new CustomerOrderLineView(line.Id, line.ArticleId, Quantity(line.OrderedQuantity),
                    Quantity(line.ShippedQuantity), Quantity(line.PendingQuantity), Quantity(remaining),
                    own.Select(reservation => new CustomerOrderReservationView(reservation.Id, reservation.State.ToString(),
                        reservation.Allocations.OrderBy(allocation => allocation.Id).Select(allocation =>
                            new CustomerOrderAllocationView(allocation.Id, allocation.ArticleId, allocation.LocationId,
                                allocation.LotId, Quantity(allocation.AllocatedQuantity), Quantity(allocation.ConsumedQuantity),
                                Quantity(allocation.ReleasedQuantity), Quantity(allocation.RemainingQuantity))).ToList())).ToList());
            }).ToList());
    }

    internal static string Quantity(decimal value) => LocationText.Quantity(value);
}

public sealed record CustomerOrderLineView(Guid Id, Guid ArticleId, string Ordered, string Shipped, string Pending,
    string ReservedRemaining, IReadOnlyList<CustomerOrderReservationView> Reservations);

public sealed record CustomerOrderReservationView(Guid Id, string State, IReadOnlyList<CustomerOrderAllocationView> Allocations);

public sealed record CustomerOrderAllocationView(Guid Id, Guid ArticleId, Guid LocationId, Guid LotId,
    string Allocated, string Consumed, string Released, string Remaining);

/// <summary>
/// GET /customer-orders/{id}/context: datos actuales que cambian por su cuenta, sin ETag (patrón
/// de GET /articles/{id}/context de HU-08). <see cref="OrderVersion"/> es el mismo token que el ETag
/// del detalle; si no coinciden, la UI leyó dos revisiones distintas y vuelve a leer ambas.
/// <see cref="CreationOperationId"/> es la operación del alta (Convergence C3, FR-026): sale del
/// OperationResult registrado, no del pedido, así que no forma parte del ETag ni del esquema.
/// </summary>
public sealed record CustomerOrderContext(string OrderVersion, DateTimeOffset AsOf,
    IReadOnlyList<CustomerOrderContextArticle> Articles, IReadOnlyList<CustomerOrderContextLocation> Locations,
    IReadOnlyList<CustomerOrderContextLot> Lots, Guid? CreationOperationId);

public sealed record CustomerOrderContextArticle(Guid Id, string Sku, string Name, string BaseUnit, bool IsActive,
    bool TracksLot, bool AllowsFractional);

public sealed record CustomerOrderContextLocation(Guid Id, string Code, string Aisle, string Rack, string Level, bool IsActive);

/// <summary>En artículos sin seguimiento, IsUntracked = true: la UI muestra «sin lote», nunca SIN-LOTE.</summary>
public sealed record CustomerOrderContextLot(Guid Id, Guid ArticleId, string Code, bool IsUntracked);

/// <summary>
/// HU-03 (D-11, FR-001a) · GET /customer-orders/lookup: el único pedido con esa referencia externa,
/// con su estado. Sin lista ni coincidencias parciales.
/// </summary>
public sealed record CustomerOrderLookup(Guid Id, string ExternalReference, string State);
