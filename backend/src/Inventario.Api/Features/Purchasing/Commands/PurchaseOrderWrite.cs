using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Inventario.Api.Infrastructure.Http;

namespace Inventario.Api.Features.Purchasing.Commands;

public sealed record PurchaseLineWrite(Guid Id, Guid ArticleId, string Quantity, string UnitPrice, DateOnly ExpectedDeliveryDate)
{
    [JsonIgnore] public decimal Amount => decimal.Parse(Quantity, CultureInfo.InvariantCulture);
    [JsonIgnore] public decimal Price => decimal.Parse(UnitPrice, CultureInfo.InvariantCulture);
}

public sealed partial record PurchaseOrderWrite(Guid SupplierId, DateOnly ExpectedDeliveryDate, PurchaseLineWrite[] Lines)
{
    public static PurchaseOrderWrite Read(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
            throw new InventoryProblemException(400, "InvalidRequest", "Se requiere un objeto JSON de pedido.");
        var errors = new Dictionary<string, string[]>();
        void Error(string field, string message) => errors[field] = [message];
        void Allowed(JsonElement value, string prefix, params string[] names)
        {
            foreach (var item in value.EnumerateObject())
                if (!names.Contains(item.Name, StringComparer.Ordinal)) Error(prefix + item.Name, "Este campo no es editable en el pedido.");
        }
        string? Text(JsonElement value, string name) => value.TryGetProperty(name, out var item)
            && item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() : null;
        Guid Id(JsonElement value, string name, string prefix)
        {
            if (Guid.TryParse(Text(value, name), out var id) && id != Guid.Empty) return id;
            Error(prefix + name, "Indica una identidad UUID válida."); return Guid.Empty;
        }
        DateOnly Date(string? value, string field)
        {
            if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
            Error(field, "Indica una fecha válida con formato año-mes-día."); return default;
        }
        string Decimal(JsonElement value, string name, string prefix, int scale, bool positive)
        {
            var raw = Text(value, name);
            if (raw is not null && DecimalText().IsMatch(raw)
                && decimal.TryParse(raw, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
                && (positive ? number > 0 : number >= 0) && decimal.Round(number, scale) == number
                && number <= (scale == 3 ? 999999999999999.999m : 9999999999999999.99m))
                return number.ToString(scale == 3 ? "F3" : "F2", CultureInfo.InvariantCulture);
            Error(prefix + name, positive ? "Indica cantidad positiva con hasta tres decimales." : "Indica precio no negativo con hasta dos decimales.");
            return "0";
        }
        Allowed(body, "", "supplierId", "expectedDeliveryDate", "lines");
        var supplier = Id(body, "supplierId", "");
        var expected = Date(Text(body, "expectedDeliveryDate"), "expectedDeliveryDate");
        var lines = new List<PurchaseLineWrite>();
        var ids = new HashSet<Guid>();
        if (!body.TryGetProperty("lines", out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0)
            Error("lines", "El pedido debe conservar al menos una línea.");
        else foreach (var item in array.EnumerateArray())
        {
            var prefix = $"lines[{lines.Count}].";
            if (item.ValueKind != JsonValueKind.Object) { Error(prefix.TrimEnd('.'), "Indica los datos de la línea."); break; }
            Allowed(item, prefix, "id", "articleId", "quantity", "unitPrice", "expectedDeliveryDate");
            var id = Id(item, "id", prefix);
            if (!ids.Add(id)) Error(prefix + "id", "Cada línea necesita una identidad distinta.");
            var article = Id(item, "articleId", prefix);
            var quantity = Decimal(item, "quantity", prefix, 3, true);
            var price = Decimal(item, "unitPrice", prefix, 2, false);
            var date = !item.TryGetProperty("expectedDeliveryDate", out var ownDate) || ownDate.ValueKind == JsonValueKind.Null
                ? expected : Date(Text(item, "expectedDeliveryDate"), prefix + "expectedDeliveryDate");
            lines.Add(new(id, article, quantity, price, date));
        }
        if (errors.Count != 0) throw new InventoryProblemException(422, "ValidationFailed", "Corrige los campos indicados del pedido.", errors);
        return new(supplier, expected, lines.ToArray());
    }

    [GeneratedRegex("^-?[0-9]+(?:\\.[0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalText();
}
