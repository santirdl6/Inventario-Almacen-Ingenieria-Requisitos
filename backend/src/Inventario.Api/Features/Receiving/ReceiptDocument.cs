using System.Globalization;
using System.Text.Json;
using Inventario.Api.Domain.Receiving;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Receiving;

internal static class ReceiptDocument
{
    public static IQueryable<Receipt> Graph(InventoryDbContext db) => db.Set<Receipt>()
        .Include(value => value.Lines).ThenInclude(value => value.Allocations).Include(value => value.Incidents).AsSplitQuery();

    public static byte[] Version(Receipt receipt) => receipt.RevisionStamp.ToByteArray();
    public static string Tag(Receipt receipt) => EntityTags.Format(Version(receipt));
    public static string Amount(decimal value) => value.ToString("F3", CultureInfo.InvariantCulture);
    private static string? OptionalAmount(decimal? value) => value is decimal number ? Amount(number) : null;
    private static JsonElement? Snapshot(string? value) => value is null ? null : JsonSerializer.Deserialize<JsonElement>(value);

    public static object Resource(Receipt receipt, object[]? corrections = null) => new
    {
        receipt.Id, receipt.PurchaseOrderId, state = receipt.State.ToString(), receipt.OccurredAt,
        receipt.SupplierDocumentReference, receipt.SupportNotApplicableReason, receipt.Reason, receipt.CounterpartyReference,
        receipt.CreatedBy, receipt.CreatedAt, receipt.RecordedAt, receipt.ActorId, receipt.OperationId, receipt.MovementId,
        corrections = corrections ?? [],
        lines = receipt.Lines.OrderBy(line => line.Id).Select(line => new
        {
            line.Id, line.PurchaseOrderLineId, reportedQuantity = Amount(line.ReportedQuantity),
            acceptedQuantity = Amount(line.AcceptedQuantity), line.DiscrepancyReason,
            orderedAtConfirmation = OptionalAmount(line.OrderedAtConfirmation), receivedBeforeConfirmation = OptionalAmount(line.ReceivedBeforeConfirmation),
            pendingAfterConfirmation = OptionalAmount(line.PendingAfterConfirmation), articleSnapshot = Snapshot(line.ArticleSnapshot),
            allocations = line.Allocations.OrderBy(value => value.Id).Select(value => new
            { value.Id, value.LocationId, value.LotId, value.LotCode, quantity = Amount(value.Quantity), locationSnapshot = Snapshot(value.LocationSnapshot) }).ToArray()
        }).ToArray(),
        incidents = receipt.Incidents.OrderBy(value => value.Id).Select(value => new
        { value.Id, value.ReceiptLineId, value.Kind, value.ObservedDescription, quantity = OptionalAmount(value.Quantity), value.ObservedUnit, value.Reason }).ToArray()
    };
}
