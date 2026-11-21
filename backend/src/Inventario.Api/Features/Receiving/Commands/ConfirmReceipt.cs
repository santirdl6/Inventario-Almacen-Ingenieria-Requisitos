using System.Text.Json;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Domain.Receiving;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.Locations;
using Inventario.Api.Features.Stock.Core;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Receiving.Commands;

public sealed class ConfirmReceipt(InventoryOperationExecutor executor)
{
    public Task<OperationResponse> ExecuteAsync(Guid key, Guid id, string? ifMatch, CancellationToken cancellation) =>
        executor.ExecuteAsync(new(key, "POST", $"/api/v1/receipts/{id:D}/confirm", "Receiving.Record", JsonSerializer.SerializeToElement(new { })),
            async (context, token) =>
            {
                var db = context.Database;
                // The order reference is immutable; read the rest only under both document locks.
                var orderId = await db.Set<Receipt>().Where(value => value.Id == id).Select(value => (Guid?)value.PurchaseOrderId).SingleOrDefaultAsync(token)
                    ?? throw new InventoryProblemException(404, "NotFound", "La recepción no existe.");
                await context.Locks.DocumentsAsync([new("purchase-order", orderId), new("receipt", id)], token);
                var receipt = await ReceiptDocument.Graph(db).SingleAsync(value => value.Id == id, token);
                if (receipt.State == ReceiptState.Confirmed) throw new InventoryProblemException(409, "DocumentAlreadyConfirmed", "La recepción ya está confirmada.");
                EntityTags.RequireMatch(ifMatch, ReceiptDocument.Version(receipt));
                var order = await db.Set<PurchaseOrder>().Include(value => value.Lines).SingleAsync(value => value.Id == orderId, token);
                var outcome = await ApplyAsync(context, receipt, order, token);
                return OperationResponse.Json(200, new { operationId = key, resource = ReceiptDocument.Resource(receipt), movementId = outcome.MovementId,
                    purchaseOrderId = order.Id, purchaseOrderVersion = EntityTags.Format(order.Version), purchaseState = order.State.ToString(),
                    lines = order.Lines.Select(line => new { purchaseOrderLineId = line.Id, receivedQuantity = ReceiptDocument.Amount(line.ReceivedQuantity),
                        pendingQuantity = ReceiptDocument.Amount(line.PendingQuantity) }).ToArray() }, receipt.Id,
                    new Dictionary<string, string> { ["ETag"] = ReceiptDocument.Tag(receipt) });
            }, cancellation);

    internal static async Task<StockOperationOutcome> ApplyAsync(OperationContext context, Receipt receipt, PurchaseOrder order, CancellationToken token)
    {
        var db = context.Database;
        if (order.SendStatus != PurchaseSendStatus.Accepted)
            throw new InventoryProblemException(409, "PurchaseNotReceivable", "El envío del pedido debe estar aceptado.");
        if (!receipt.Lines.Any(line => line.AcceptedQuantity > 0)) throw ReceiptWrite.Invalid("lines", "Acepta una cantidad positiva en al menos una línea.");
        if (receipt.Reason is null) throw ReceiptWrite.Invalid("reason", "Indica el motivo de entrada.");
        if (receipt.SupplierDocumentReference is null && receipt.SupportNotApplicableReason is null)
            throw ReceiptWrite.Invalid("supplierDocumentReference", "Indica soporte o motivo de su ausencia.");
        var orderLines = order.Lines.ToDictionary(value => value.Id);
        if (receipt.Lines.Any(line => !orderLines.ContainsKey(line.PurchaseOrderLineId)))
            throw ReceiptWrite.Invalid("lines", "Las líneas deben pertenecer al pedido.");
        var locationIds = receipt.Lines.SelectMany(line => line.Allocations).Select(value => value.LocationId).Distinct().ToArray();
        var articleIds = receipt.Lines.Select(line => orderLines[line.PurchaseOrderLineId].ArticleId).Distinct().ToArray();
        await context.Locks.LocationsAsync(locationIds, token);
        await context.Locks.ArticlesAsync(articleIds, token);
        var locations = await db.Set<Location>().Where(value => locationIds.Contains(value.Id)).ToDictionaryAsync(value => value.Id, token);
        var articles = await db.Articles.Where(value => articleIds.Contains(value.Id)).ToDictionaryAsync(value => value.Id, token);
        var usage = context.Services.GetRequiredService<ILocationUsage>();
        foreach (var location in locationIds)
            if (await usage.IsInOpenCountAsync(location, token)) throw new InventoryProblemException(409, "CountScopeFrozen", "La ubicación está en un conteo abierto.");
        var assignments = new List<(ReceiptAllocation Allocation, Guid Article, string LotCode)>();
        foreach (var (line, index) in receipt.Lines.OrderBy(line => line.Id).Select((line, i) => (line, i)))
        {
            var purchaseLine = orderLines[line.PurchaseOrderLineId];
            var article = articles[purchaseLine.ArticleId]; var path = $"lines[{index}]";
            if (line.AcceptedQuantity > purchaseLine.PendingQuantity)
                throw new InventoryProblemException(409, "OverReceipt", $"La línea {purchaseLine.Id:D} tiene {ReceiptDocument.Amount(purchaseLine.PendingQuantity)} pendientes.");
            if (line.Allocations.Sum(item => item.Quantity) != line.AcceptedQuantity)
                throw ReceiptWrite.Invalid(path + ".allocations", "La distribución debe sumar la cantidad aceptada.");
            if (line.AcceptedQuantity > 0 && !article.IsActive)
                throw new InventoryProblemException(409, "ArticleInactive", "El artículo está inactivo.");
            foreach (var (assignment, offset) in line.Allocations.OrderBy(item => item.Id).Select((item, i) => (item, i)))
            {
                var field = $"{path}.allocations[{offset}]";
                if (!locations.TryGetValue(assignment.LocationId, out var location) || !location.IsActive)
                    throw ReceiptWrite.Invalid(field + ".locationId", "Selecciona una ubicación activa.");
                var code = assignment.LotCode ?? Lot.UntrackedCode;
                if (article.TracksLot == (code == Lot.UntrackedCode))
                    throw ReceiptWrite.Invalid(field + ".lotCode", article.TracksLot ? "Indica el lote del artículo." : "El artículo no admite lote.");
                assignments.Add((assignment, article.Id, code));
            }
        }

        var supplier = JsonSerializer.Deserialize<PurchaseSupplierSnapshot>(order.SupplierSnapshot, OperationResponse.JsonOptions)!;
        var command = StockCore.Prepare(StockOperation.Receive,
            new(new("receipt", receipt.Id), receipt.OccurredAt, receipt.Reason, receipt.SupplierDocumentReference, receipt.SupportNotApplicableReason, supplier.Code),
            assignments.GroupBy(item => (item.Article, item.Allocation.LocationId, item.LotCode)).Select(bucket =>
                new StockAllocation(bucket.Key.Article, bucket.Key.LocationId, bucket.Key.LotCode, bucket.Sum(item => item.Allocation.Quantity))).ToArray());

        // Obtain the purchase's SQL rowversion in this transaction before staging entities
        // that reference OperationResult. Nothing is committed by this flush.
        var history = receipt.Lines.ToDictionary(line => line.Id, line => orderLines[line.PurchaseOrderLineId].ReceivedQuantity);
        foreach (var line in receipt.Lines)
        {
            var purchaseLine = orderLines[line.PurchaseOrderLineId];
            purchaseLine.SetReceivedQuantity(purchaseLine.ReceivedQuantity + line.AcceptedQuantity);
        }
        order.DeriveReceiptState();
        await db.SaveChangesAsync(token);

        var outcome = await context.Services.GetRequiredService<StockCore>().ApplyAsync(context, command, token);
        var lots = db.Set<Lot>().Local.ToDictionary(lot => (lot.ArticleId, lot.Code), lot => lot.Id);
        foreach (var item in assignments)
            item.Allocation.Freeze(lots[(item.Article, item.LotCode)], item.LotCode,
                JsonSerializer.Serialize(locations[item.Allocation.LocationId].AuditSnapshot(), OperationResponse.JsonOptions));
        foreach (var line in receipt.Lines)
        {
            var ordered = orderLines[line.PurchaseOrderLineId];
            line.Freeze(ordered.OrderedQuantity, history[line.Id], ordered.ArticleSnapshot);
        }
        receipt.Confirm(context.ActorId, context.OperationId, outcome.MovementId, outcome.RecordedAt, supplier.Code);
        return outcome;
    }
}
