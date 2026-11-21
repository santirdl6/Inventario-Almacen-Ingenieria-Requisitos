using System.Text.Json;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Domain.Receiving;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Receiving.Commands;

public sealed class SaveReceiptDraft(InventoryOperationExecutor executor)
{
    public Task<OperationResponse> ExecuteAsync(Guid key, Guid? id, ReceiptWrite write, string? ifMatch, CancellationToken cancellation)
    {
        var receiptId = id ?? Guid.NewGuid();
        var resource = $"/api/v1/receipts/{receiptId:D}";
        return executor.ExecuteAsync(new(key, id is null ? "POST" : "PUT", id is null ? "/api/v1/receipts" : resource,
            "Receiving.Record", JsonSerializer.SerializeToElement(write, OperationResponse.JsonOptions)), async (context, token) =>
        {
            var db = context.Database;
            await context.Locks.DocumentsAsync([new("purchase-order", write.PurchaseOrderId), new("receipt", receiptId)], token);
            var current = id is null ? null : await ReceiptDocument.Graph(db).SingleOrDefaultAsync(value => value.Id == receiptId, token);
            if (id is not null && current is null) throw new InventoryProblemException(404, "NotFound", "La recepción no existe.");
            if (current is not null)
            {
                EntityTags.RequireMatch(ifMatch, ReceiptDocument.Version(current));
                if (current.State == ReceiptState.Confirmed) throw new InventoryProblemException(409, "DocumentAlreadyConfirmed", "La recepción ya está confirmada.");
                if (current.PurchaseOrderId != write.PurchaseOrderId) throw ReceiptWrite.Invalid("purchaseOrderId", "No puedes cambiar el pedido del borrador.");
            }

            var order = await db.Set<PurchaseOrder>().Include(value => value.Lines).SingleOrDefaultAsync(value => value.Id == write.PurchaseOrderId, token)
                ?? throw ReceiptWrite.Invalid("purchaseOrderId", "No existe el pedido de compra.");
            if (order.SendStatus != PurchaseSendStatus.Accepted || order.State is not (PurchaseOrderState.Sent or PurchaseOrderState.PartiallyReceived))
                throw new InventoryProblemException(409, "PurchaseNotReceivable", "Selecciona un pedido enviado con cantidades pendientes.");
            var ordered = order.Lines.ToDictionary(line => line.Id);
            var lineReferences = write.Lines.Select(line => line.PurchaseOrderLineId)
                .Concat(current?.Lines.Select(line => line.PurchaseOrderLineId) ?? []).Distinct().ToArray();
            if (lineReferences.Any(line => !ordered.ContainsKey(line))) throw ReceiptWrite.Invalid("lines", "Las líneas deben pertenecer al pedido.");
            var assignments = write.Lines.SelectMany(line => line.Allocations).ToArray();
            var places = assignments.Select(item => item.LocationId).Distinct().ToArray();
            await context.Locks.LocationsAsync(places.Concat(current?.Lines.SelectMany(line => line.Allocations).Select(item => item.LocationId) ?? []), token);
            var articles = lineReferences.Select(line => ordered[line].ArticleId).Distinct().ToArray();
            await context.Locks.ArticlesAsync(articles, token);
            var definitions = await db.Articles.Where(value => articles.Contains(value.Id)).ToDictionaryAsync(value => value.Id, token);
            if (await db.Set<Location>().CountAsync(value => places.Contains(value.Id), token) != places.Length)
                throw ReceiptWrite.Invalid("allocations", "Alguna ubicación no existe.");

            var receipt = current ?? new Receipt(receiptId, order.Id, context.ActorId, DateTimeOffset.UtcNow);
            var oldLines = receipt.Lines.ToDictionary(value => value.Id);
            var oldIncidents = receipt.Incidents.ToDictionary(value => value.Id);
            var oldAllocations = receipt.Lines.SelectMany(value => value.Allocations).ToDictionary(value => value.Id);
            var newLineIds = write.Lines.Where(value => !oldLines.ContainsKey(value.Id)).Select(value => value.Id).ToArray();
            var newAllocationIds = assignments.Where(value => !oldAllocations.ContainsKey(value.Id)).Select(value => value.Id).ToArray();
            var newIncidentIds = write.Incidents.Where(value => !oldIncidents.ContainsKey(value.Id)).Select(value => value.Id).ToArray();
            if (await db.Set<ReceiptLine>().AnyAsync(value => newLineIds.Contains(value.Id), token) ||
                await db.Set<ReceiptAllocation>().AnyAsync(value => newAllocationIds.Contains(value.Id), token) ||
                await db.Set<ReceiptIncident>().AnyAsync(value => newIncidentIds.Contains(value.Id), token))
                throw ReceiptWrite.Invalid("lines", "Algún identificador ya pertenece a otra recepción.");

            var retainedLines = write.Lines.Select(value => value.Id).ToHashSet();
            var retainedAllocations = assignments.Select(value => value.Id).ToHashSet();
            var retainedIncidents = write.Incidents.Select(value => value.Id).ToHashSet();
            var removed = oldLines.Values.Where(value => !retainedLines.Contains(value.Id)).ToArray();
            // Delete FK dependants first. Preserve existing incidents by unlinking any line removed in this edit.
            if (removed.Length != 0)
            {
                var removedIds = removed.Select(value => value.Id).ToHashSet();
                foreach (var incident in oldIncidents.Values.Where(value => retainedIncidents.Contains(value.Id) && value.ReceiptLineId is Guid old && removedIds.Contains(old)))
                    incident.Revise(null, incident.Kind, incident.ObservedDescription, incident.Quantity, incident.ObservedUnit, incident.Reason);
            }
            db.RemoveRange(oldIncidents.Values.Where(value => !retainedIncidents.Contains(value.Id)));
            db.RemoveRange(oldAllocations.Values.Where(value => !retainedAllocations.Contains(value.Id)));
            db.RemoveRange(removed);
            // A replacement may use the same purchase line under a new receipt-line ID.
            if (removed.Length != 0) await db.SaveChangesAsync(token);
            var lines = new List<ReceiptLine>();
            for (var i = 0; i < write.Lines.Length; i++)
            {
                var value = write.Lines[i];
                var line = oldLines.GetValueOrDefault(value.Id) ?? new ReceiptLine(value.Id, receipt.Id, value.PurchaseOrderLineId);
                if (line.PurchaseOrderLineId != value.PurchaseOrderLineId) throw ReceiptWrite.Invalid($"lines[{i}].purchaseOrderLineId", "Usa una línea nueva para otra referencia de compra.");
                var definition = definitions[ordered[value.PurchaseOrderLineId].ArticleId];
                if (!definition.AllowsFractional && (decimal.Truncate(value.ReportedQuantity) != value.ReportedQuantity ||
                    decimal.Truncate(value.AcceptedQuantity) != value.AcceptedQuantity || value.Allocations.Any(item => decimal.Truncate(item.Quantity) != item.Quantity)))
                    throw ReceiptWrite.Invalid($"lines[{i}].acceptedQuantity", "El artículo solo admite cantidades enteras.");
                var allocations = value.Allocations.Select(item =>
                {
                    var allocation = oldAllocations.GetValueOrDefault(item.Id) ?? new ReceiptAllocation(item.Id, line.Id);
                    if (allocation.ReceiptLineId != line.Id) throw ReceiptWrite.Invalid($"lines[{i}].allocations", "La asignación no pertenece a esta línea.");
                    allocation.Revise(item.LocationId, item.LotCode, item.Quantity);
                    if (current is not null && !oldAllocations.ContainsKey(item.Id)) db.Add(allocation);
                    return allocation;
                }).ToArray();
                line.Revise(value.ReportedQuantity, value.AcceptedQuantity, value.DiscrepancyReason, allocations);
                if (current is not null && !oldLines.ContainsKey(line.Id)) db.Add(line);
                lines.Add(line);
            }
            var incidents = write.Incidents.Select(value =>
            {
                var incident = oldIncidents.GetValueOrDefault(value.Id) ?? new ReceiptIncident(value.Id, receipt.Id);
                incident.Revise(value.ReceiptLineId, value.Kind, value.ObservedDescription, value.Quantity, value.ObservedUnit, value.Reason);
                if (current is not null && !oldIncidents.ContainsKey(value.Id)) db.Add(incident);
                return incident;
            }).ToArray();
            receipt.Replace(write.OccurredAt, write.SupplierDocumentReference, write.SupportNotApplicableReason, write.Reason, lines, incidents);
            if (current is null) db.Add(receipt);
            var headers = new Dictionary<string, string> { ["ETag"] = ReceiptDocument.Tag(receipt) };
            if (current is null) headers["Location"] = resource;
            return OperationResponse.Json(current is null ? 201 : 200, new { operationId = key, resource = ReceiptDocument.Resource(receipt) }, receipt.Id, headers);
        }, cancellation);
    }
}
