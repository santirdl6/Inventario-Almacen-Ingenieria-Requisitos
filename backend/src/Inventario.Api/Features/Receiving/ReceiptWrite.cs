using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Inventario.Api.Infrastructure.Http;

namespace Inventario.Api.Features.Receiving;

public sealed record ReceiptWrite(Guid PurchaseOrderId, DateTimeOffset OccurredAt, string? SupplierDocumentReference,
    string? SupportNotApplicableReason, string? Reason, ReceiptLineWrite[] Lines, ReceiptIncidentWrite[] Incidents)
{
    private static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static ReceiptWrite Read(JsonElement json)
    {
        try
        {
            var input = json.Deserialize<DraftInput>(Strict) ?? throw Invalid("receipt", "Indica los datos de la recepción.");
            var lines = (input.Lines ?? []).Select((line, index) => ReadLine(line, $"lines[{index}]")).ToArray();
            var incidents = (input.Incidents ?? []).Select((item, index) =>
            {
                if (item is null) throw Invalid($"incidents[{index}]", "Indica los datos de la incidencia.");
                var field = $"incidents[{index}]";
                return new ReceiptIncidentWrite(RequiredId(item.Id, field + ".id"), item.ReceiptLineId,
                    Text(item.Kind, 40, field + ".kind", true)!, Text(item.ObservedDescription, 500, field + ".observedDescription", true)!,
                    item.Quantity is null ? null : Quantity(item.Quantity, field + ".quantity", true),
                    Text(item.ObservedUnit, 32, field + ".observedUnit"), Text(item.Reason, 500, field + ".reason", true)!);
            }).ToArray();
            Unique(lines.Select(line => line.Id), "lines");
            Unique(lines.Select(line => line.PurchaseOrderLineId), "lines");
            Unique(lines.SelectMany(line => line.Allocations).Select(value => value.Id), "allocations");
            Unique(incidents.Select(item => item.Id), "incidents");
            var lineIds = lines.Select(line => line.Id).ToHashSet();
            if (incidents.Any(item => item.ReceiptLineId is Guid id && !lineIds.Contains(id)))
                throw Invalid("incidents", "La línea de la incidencia debe pertenecer a la recepción.");
            var support = Text(input.SupplierDocumentReference, 200, "supplierDocumentReference");
            var absence = Text(input.SupportNotApplicableReason, 500, "supportNotApplicableReason");
            if (support is not null && absence is not null) throw Invalid("supplierDocumentReference", "Indica soporte o motivo de ausencia, no ambos.");
            return new(RequiredId(input.PurchaseOrderId, "purchaseOrderId"), ParseDate(input.OccurredAt, "occurredAt"), support, absence,
                Text(input.Reason, 500, "reason"), lines, incidents);
        }
        catch (JsonException error) { throw Invalid(error.Path ?? "receipt", "El campo no está permitido o su formato no es válido."); }
    }

    private static ReceiptLineWrite ReadLine(LineInput? input, string field)
    {
        if (input is null) throw Invalid(field, "Indica una línea válida.");
        var reported = Quantity(input.ReportedQuantity, field + ".reportedQuantity");
        var accepted = Quantity(input.AcceptedQuantity, field + ".acceptedQuantity");
        if (accepted > reported) throw Invalid(field + ".acceptedQuantity", "Lo aceptado no puede superar lo reportado.");
        var discrepancy = Text(input.DiscrepancyReason, 500, field + ".discrepancyReason", reported > accepted);
        var allocations = (input.Allocations ?? []).Select((item, index) =>
        {
            var path = $"{field}.allocations[{index}]";
            if (item is null) throw Invalid(path, "Indica los datos de la asignación.");
            return new ReceiptAllocationWrite(RequiredId(item.Id, path + ".id"), RequiredId(item.LocationId, path + ".locationId"),
                Text(item.LotCode, 64, path + ".lotCode")?.ToUpperInvariant(), Quantity(item.Quantity, path + ".quantity", true));
        }).ToArray();
        if (accepted == 0 && allocations.Length != 0) throw Invalid(field + ".allocations", "Una línea sin aceptación no admite asignaciones.");
        return new(RequiredId(input.Id, field + ".id"), RequiredId(input.PurchaseOrderLineId, field + ".purchaseOrderLineId"), reported, accepted, discrepancy, allocations);
    }

    internal static DateTimeOffset ParseDate(string? value, string field) => value is not null
        && Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}T.+(?:Z|[+-]\d\d:\d\d)$", RegexOptions.CultureInvariant)
        && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
        ? parsed.ToUniversalTime() : throw Invalid(field, "Indica una fecha ISO 8601 con zona horaria.");

    internal static InventoryProblemException Invalid(string field, string message) => InventoryProblemException.Validation(field, message);
    private static Guid RequiredId(Guid id, string field) => id == Guid.Empty ? throw Invalid(field, "Indica un identificador válido.") : id;
    private static void Unique(IEnumerable<Guid> values, string field)
    {
        var ids = new HashSet<Guid>();
        if (values.Any(value => !ids.Add(value))) throw Invalid(field, "No se permiten identificadores repetidos.");
    }
    private static string? Text(string? value, int max, string field, bool required = false)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return required ? throw Invalid(field, "Este campo es obligatorio.") : null;
        if (value.Length > max || value.Any(char.IsControl)) throw Invalid(field, $"Indica texto válido de hasta {max} caracteres.");
        return value;
    }
    private static decimal Quantity(string? value, string field, bool positive = false)
    {
        if (value is null || !Regex.IsMatch(value, @"^[0-9]+(?:\.[0-9]+)?$", RegexOptions.CultureInvariant) ||
            (value.Contains('.') && value.TrimEnd('0').Split('.')[1].Length > 3) ||
            !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) ||
            parsed > 999999999999999.999m || (positive && parsed <= 0))
            throw Invalid(field, "Indica una cantidad válida con hasta tres decimales.");
        return decimal.Round(parsed, 3);
    }

    private sealed record DraftInput(Guid PurchaseOrderId, string? OccurredAt, string? SupplierDocumentReference,
        string? SupportNotApplicableReason, string? Reason, LineInput?[]? Lines, IncidentInput?[]? Incidents);
    private sealed record LineInput(Guid Id, Guid PurchaseOrderLineId, string? ReportedQuantity, string? AcceptedQuantity,
        string? DiscrepancyReason, AllocationInput?[]? Allocations);
    private sealed record AllocationInput(Guid Id, Guid LocationId, string? LotCode, string? Quantity);
    private sealed record IncidentInput(Guid Id, Guid? ReceiptLineId, string? Kind, string? ObservedDescription,
        string? Quantity, string? ObservedUnit, string? Reason);
}

public sealed record ReceiptLineWrite(Guid Id, Guid PurchaseOrderLineId, decimal ReportedQuantity, decimal AcceptedQuantity,
    string? DiscrepancyReason, ReceiptAllocationWrite[] Allocations);
public sealed record ReceiptAllocationWrite(Guid Id, Guid LocationId, string? LotCode, decimal Quantity);
public sealed record ReceiptIncidentWrite(Guid Id, Guid? ReceiptLineId, string Kind, string ObservedDescription,
    decimal? Quantity, string? ObservedUnit, string Reason);
