using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Domain.Receiving;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Receiving.Commands;

public sealed class ReconcileReceiptVoucher(InventoryOperationExecutor executor)
{
    internal static string NormalizeVoucher(string? input)
    {
        var value = input?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(value) || value.Length > 200 || value.Any(char.IsControl))
            throw ReceiptWrite.Invalid("voucherId", "Indica un comprobante válido de hasta 200 caracteres.");
        return value;
    }

    // Lock resources are UUID-based. Hash only establishes a stable shared lock;
    // the unique database key remains authoritative for the full voucher identifier.
    internal static Guid VoucherLock(string voucher) => new(SHA256.HashData(Encoding.UTF8.GetBytes(voucher))[..16]);

    public Task<OperationResponse> ExecuteAsync(Guid key, JsonElement input, CancellationToken cancellation)
    {
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Any(value => value.Name is not ("type" or "voucherId" or "responsibleReference" or "receipt")))
            throw ReceiptWrite.Invalid("contingency", "Indica un comprobante de recepción válido.");
        static string? GetString(JsonElement obj, string name) => obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (GetString(input, "type") != "Receive" || !input.TryGetProperty("receipt", out var receiptInput))
            throw ReceiptWrite.Invalid("type", "Selecciona una recepción de contingencia.");
        var voucher = NormalizeVoucher(GetString(input, "voucherId"));
        var responsible = GetString(input, "responsibleReference")?.Trim();
        if (string.IsNullOrEmpty(responsible) || responsible.Length > 200 || responsible.Any(char.IsControl))
            throw ReceiptWrite.Invalid("responsibleReference", "Indica quién registró el comprobante.");
        var write = ReceiptWrite.Read(receiptInput);
        var normalized = JsonSerializer.Serialize(new { type = "Receive", voucherId = voucher, responsibleReference = responsible, receipt = write }, OperationResponse.JsonOptions);
        var documentId = Guid.NewGuid();
        return executor.ExecuteAsync(new(key, "POST", "/api/v1/contingencies/reconcile", PermissionCatalog.ReceivingReconcilePolicy,
            JsonSerializer.Deserialize<JsonElement>(normalized)), async (context, token) =>
        {
            var db = context.Database;
            await context.Locks.DocumentsAsync([new("purchase-order", write.PurchaseOrderId), new("receipt", documentId),
                new("contingency-voucher", VoucherLock(voucher))], token);
            var existing = await db.Set<ContingencyReference>().AsNoTracking().SingleOrDefaultAsync(value => value.VoucherId == voucher, token);
            if (existing is not null)
                throw new InventoryProblemException(409, existing.NormalizedData == normalized ? "ContingencyAlreadyReconciled" : "ContingencyConflict",
                    existing.NormalizedData == normalized ? $"El comprobante ya se concilió en la recepción {existing.ReceiptId:D}." : "El comprobante ya corresponde a otros datos.");
            var order = await db.Set<PurchaseOrder>().Include(value => value.Lines).SingleOrDefaultAsync(value => value.Id == write.PurchaseOrderId, token)
                ?? throw ReceiptWrite.Invalid("purchaseOrderId", "No existe el pedido de compra.");
            var orderLines = order.Lines.ToDictionary(value => value.Id);
            if (write.Lines.Any(value => !orderLines.ContainsKey(value.PurchaseOrderLineId)))
                throw ReceiptWrite.Invalid("lines", "Las líneas deben pertenecer al pedido.");
            var lineIds = write.Lines.Select(value => value.Id).ToArray();
            var allocationIds = write.Lines.SelectMany(value => value.Allocations).Select(value => value.Id).ToArray();
            var incidentIds = write.Incidents.Select(value => value.Id).ToArray();
            if (await db.Set<ReceiptLine>().AnyAsync(value => lineIds.Contains(value.Id), token) ||
                await db.Set<ReceiptAllocation>().AnyAsync(value => allocationIds.Contains(value.Id), token) ||
                await db.Set<ReceiptIncident>().AnyAsync(value => incidentIds.Contains(value.Id), token))
                throw ReceiptWrite.Invalid("lines", "Algún identificador ya pertenece a otra recepción.");
            var locationIds = write.Lines.SelectMany(value => value.Allocations).Select(value => value.LocationId).Distinct().ToArray();
            var articleIds = write.Lines.Select(value => orderLines[value.PurchaseOrderLineId].ArticleId).Distinct().ToArray();
            await context.Locks.LocationsAsync(locationIds, token);
            await context.Locks.ArticlesAsync(articleIds, token);
            if (await db.Set<Location>().CountAsync(value => locationIds.Contains(value.Id), token) != locationIds.Length)
                throw ReceiptWrite.Invalid("allocations", "Alguna ubicación no existe.");
            var receipt = new Receipt(documentId, order.Id, context.ActorId, DateTimeOffset.UtcNow);
            var lines = write.Lines.Select(value =>
            {
                var line = new ReceiptLine(value.Id, receipt.Id, value.PurchaseOrderLineId);
                var allocations = value.Allocations.Select(item =>
                {
                    var allocation = new ReceiptAllocation(item.Id, line.Id);
                    allocation.Revise(item.LocationId, item.LotCode, item.Quantity);
                    return allocation;
                }).ToArray();
                line.Revise(value.ReportedQuantity, value.AcceptedQuantity, value.DiscrepancyReason, allocations);
                return line;
            }).ToArray();
            var incidents = write.Incidents.Select(value =>
            {
                var incident = new ReceiptIncident(value.Id, receipt.Id);
                incident.Revise(value.ReceiptLineId, value.Kind, value.ObservedDescription, value.Quantity, value.ObservedUnit, value.Reason);
                return incident;
            }).ToArray();
            receipt.Replace(write.OccurredAt, write.SupplierDocumentReference, write.SupportNotApplicableReason, write.Reason, lines, incidents);
            db.Add(receipt);
            var outcome = await ConfirmReceipt.ApplyAsync(context, receipt, order, token);
            db.Add(new ContingencyReference(voucher, receipt.OccurredAt, responsible, normalized, receipt.Id, key, context.ActorId, outcome.RecordedAt));
            return OperationResponse.Json(201, new { operationId = key, voucherId = voucher, resource = ReceiptDocument.Resource(receipt), movementId = outcome.MovementId },
                receipt.Id, new Dictionary<string, string> { ["Location"] = $"/api/v1/receipts/{receipt.Id:D}", ["ETag"] = ReceiptDocument.Tag(receipt) });
        }, cancellation);
    }
}
