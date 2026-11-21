using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Domain.Receiving;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.Locations;
using Inventario.Api.Features.Stock.Core;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Receiving.Commands;

public sealed class CorrectReceipt(InventoryOperationExecutor executor)
{
    private sealed record Input(Guid CorrectionId, string? Reason, string? Reference, Line[]? Lines);
    private sealed record Line(Guid ReceiptAllocationId, string? Quantity);
    private sealed record Normalized(Guid CorrectionId, string Reason, string? Reference, CorrectionLine[] Lines);
    private sealed record CorrectionLine(Guid ReceiptAllocationId, decimal Quantity);

    public Task<OperationResponse> ExecuteAsync(Guid key, Guid movementId, JsonElement body, string? tag, CancellationToken cancellation)
    {
        Input input;
        try { input = body.Deserialize<Input>(new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })!; }
        catch (JsonException) { throw ReceiptWrite.Invalid("correction", "Corrige los datos de compensación."); }
        if (input is null || input.CorrectionId == Guid.Empty || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 500 || input.Lines is null || input.Lines.Length == 0)
            throw ReceiptWrite.Invalid("correction", "Indica identificador, motivo y asignaciones.");
        if (input.Reference?.Trim().Length > 200 || input.Reference?.Any(char.IsControl) == true)
            throw ReceiptWrite.Invalid("reference", "Indica una referencia válida de hasta 200 caracteres.");
        var lines = input.Lines.Select(item =>
        {
            if (item is null || item.ReceiptAllocationId == Guid.Empty || item.Quantity is null ||
                !decimal.TryParse(item.Quantity, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity) ||
                quantity <= 0 || quantity > 999999999999999.999m || decimal.Round(quantity, 3) != quantity)
                throw ReceiptWrite.Invalid("lines", "Indica asignaciones y cantidades positivas con hasta tres decimales.");
            return new CorrectionLine(item.ReceiptAllocationId, quantity);
        }).ToArray();
        if (lines.Select(value => value.ReceiptAllocationId).Distinct().Count() != lines.Length)
            throw ReceiptWrite.Invalid("lines", "No repitas asignaciones.");
        var reference = input.Reference?.Trim();
        var normalized = new Normalized(input.CorrectionId, input.Reason.Trim(), string.IsNullOrEmpty(reference) ? null : reference, lines);
        return executor.ExecuteAsync(new(key, "POST", $"/api/v1/movements/{movementId:D}/corrections", "Inventory.Adjust",
            JsonSerializer.SerializeToElement(normalized, OperationResponse.JsonOptions)), async (context, token) =>
        {
            var db = context.Database;
            var original = await db.Set<StockMovement>().AsNoTracking().SingleOrDefaultAsync(value => value.Id == movementId && value.Kind == MovementKind.Receipt, token)
                ?? throw new InventoryProblemException(404, "NotFound", "No existe la entrada de recepción.");
            var receiptInfo = await db.Set<Receipt>().AsNoTracking().Where(value => value.Id == original.SourceDocumentId && value.MovementId == movementId)
                .Select(value => new { value.Id, value.PurchaseOrderId }).SingleOrDefaultAsync(token)
                ?? throw new InventoryProblemException(404, "NotFound", "No existe la recepción de origen.");
            await context.Locks.DocumentsAsync([new("purchase-order", receiptInfo.PurchaseOrderId), new("receipt", receiptInfo.Id),
                new("receipt-correction", normalized.CorrectionId)], token);
            if (await db.Set<ReceiptCorrection>().AnyAsync(value => value.Id == normalized.CorrectionId, token))
                throw new InventoryProblemException(409, "DocumentAlreadyConfirmed", "La compensación ya fue registrada.");
            var receipt = await ReceiptDocument.Graph(db).SingleAsync(value => value.Id == receiptInfo.Id, token);
            EntityTags.RequireMatch(tag, ReceiptDocument.Version(receipt));
            var order = await db.Set<PurchaseOrder>().Include(value => value.Lines).SingleAsync(value => value.Id == receiptInfo.PurchaseOrderId, token);
            var orderLines = order.Lines.ToDictionary(value => value.Id);
            var originals = receipt.Lines.SelectMany(line => line.Allocations.Select(allocation => (line, allocation))).ToDictionary(value => value.allocation.Id);
            if (lines.Any(value => !originals.ContainsKey(value.ReceiptAllocationId))) throw ReceiptWrite.Invalid("lines", "Las asignaciones deben pertenecer a la recepción original.");
            var selected = lines.Select(value => (value, original: originals[value.ReceiptAllocationId])).ToArray();
            var locationIds = selected.Select(value => value.original.allocation.LocationId).Distinct().ToArray();
            var articleIds = selected.Select(value => orderLines[value.original.line.PurchaseOrderLineId].ArticleId).Distinct().ToArray();
            await context.Locks.LocationsAsync(locationIds, token);
            await context.Locks.ArticlesAsync(articleIds, token);
            foreach (var location in locationIds)
                if (await context.Services.GetRequiredService<ILocationUsage>().IsInOpenCountAsync(location, token))
                    throw new InventoryProblemException(409, "CountScopeFrozen", "La ubicación está en un conteo abierto.");
            var allocationIds = lines.Select(value => value.ReceiptAllocationId).ToArray();
            var previous = await db.Set<ReceiptCorrectionLine>().Where(value => allocationIds.Contains(value.ReceiptAllocationId))
                .GroupBy(value => value.ReceiptAllocationId).Select(group => new { Id = group.Key, Total = group.Sum(value => value.Quantity) }).ToDictionaryAsync(value => value.Id, value => value.Total, token);
            foreach (var item in selected)
            {
                if (item.original.allocation.LotId is null || item.value.Quantity + previous.GetValueOrDefault(item.value.ReceiptAllocationId) > item.original.allocation.Quantity)
                    throw new InventoryProblemException(409, "CorrectionExceedsOriginal", "La compensación supera la asignación disponible para corregir.");
            }
            var buckets = selected.GroupBy(item => (orderLines[item.original.line.PurchaseOrderLineId].ArticleId,
                item.original.allocation.LocationId, Lot: item.original.allocation.LotId!.Value)).Select(group =>
                new ReceiptCorrectionBucket(group.Key.ArticleId, group.Key.LocationId, group.Key.Lot, group.Sum(item => item.value.Quantity),
                    JsonSerializer.Deserialize<PurchaseArticleSnapshot>(orderLines[group.First().original.line.PurchaseOrderLineId].ArticleSnapshot, OperationResponse.JsonOptions)!.BaseUnit)).ToArray();
            foreach (var bucket in buckets)
            {
                var dependent = await (from entry in db.Set<StockEntry>() join movement in db.Set<StockMovement>() on entry.MovementId equals movement.Id
                    where entry.ArticleId == bucket.ArticleId && entry.LocationId == bucket.LocationId && entry.LotId == bucket.LotId
                        && entry.PhysicalDelta < 0 && movement.RecordedAt >= original.RecordedAt
                        && !(movement.Kind == MovementKind.Correction && movement.OriginalMovementId == original.Id)
                    select entry.Id).AnyAsync(token);
                if (dependent) throw new InventoryProblemException(409, "CorrectionDependency", "Hay movimientos posteriores que requieren conciliación antes de compensar.");
            }
            foreach (var group in selected.GroupBy(item => item.original.line.PurchaseOrderLineId))
            {
                var purchase = orderLines[group.Key];
                purchase.SetReceivedQuantity(purchase.ReceivedQuantity - group.Sum(item => item.value.Quantity));
            }
            order.DeriveReceiptState(); receipt.Touch();
            await db.SaveChangesAsync(token);
            var outcome = await context.Services.GetRequiredService<StockCore>().CorrectReceiptAsync(context, normalized.CorrectionId, original, normalized.Reason, buckets, token);
            db.Add(new ReceiptCorrection(normalized.CorrectionId, receipt.Id, original.Id, outcome.MovementId, context.ActorId, context.OperationId, outcome.RecordedAt, normalized.Reason, normalized.Reference));
            db.AddRange(lines.Select(item => new ReceiptCorrectionLine(normalized.CorrectionId, item.ReceiptAllocationId, item.Quantity)));
            return OperationResponse.Json(201, new { operationId = key, correctionId = normalized.CorrectionId, movementId = outcome.MovementId, receiptId = receipt.Id,
                purchaseOrderVersion = EntityTags.Format(order.Version), purchaseState = order.State.ToString() }, receipt.Id,
                new Dictionary<string, string> { ["ETag"] = ReceiptDocument.Tag(receipt) });
        }, cancellation);
    }
}
