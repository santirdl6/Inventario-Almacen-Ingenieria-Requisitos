using System.Security.Cryptography;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Features.Purchasing.Queries;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Messaging;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Purchasing.Commands;

public sealed record PurchaseSendMutation(Guid OperationId, Guid PurchaseOrderId, Guid DeliveryId, string State, string SendStatus);

public sealed class RequestPurchaseOrderSend(InventoryOperationExecutor executor)
{
    public Task<OperationResponse> ExecuteAsync(Guid operation, Guid id, JsonElement body, string? ifMatch, CancellationToken cancellation)
    {
        if (body.ValueKind != JsonValueKind.Object) throw new InventoryProblemException(400, "InvalidRequest", "Se requiere una solicitud de envío.");
        foreach (var property in body.EnumerateObject())
            if (property.Name is not ("kind" or "reviewToken")) throw InventoryProblemException.Validation(property.Name, "Este campo no cambia el documento de envío.");
        if (!body.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != "initial")
            throw InventoryProblemException.Validation("kind", "Indica una primera solicitud de envío.");
        var reviewToken = body.TryGetProperty("reviewToken", out var supplied) && supplied.ValueKind == JsonValueKind.String ? supplied.GetString()?.Trim() : null;
        try { if (reviewToken is null || Convert.FromBase64String(reviewToken).Length != 32) throw new FormatException(); }
        catch (FormatException) { throw InventoryProblemException.Validation("reviewToken", "Vuelve a revisar el documento y destinatario antes de enviar."); }
        var deliveryId = Guid.NewGuid();
        return executor.ExecuteAsync(new(operation, "POST", $"/api/v1/purchase-orders/{id:D}/send", "Purchasing.Send",
            JsonSerializer.SerializeToElement(new { kind = "initial", reviewToken }, OperationResponse.JsonOptions)), async (context, token) =>
        {
            var db = context.Database;
            await context.Locks.DocumentsAsync([new("purchase-order", id)], token);
            var order = await db.Set<PurchaseOrder>().Include(value => value.Lines).SingleOrDefaultAsync(value => value.Id == id, token)
                ?? throw new InventoryProblemException(404, "NotFound", "El pedido no existe.");
            EntityTags.RequireMatch(ifMatch, order.Version);
            if (!order.IsEditable) throw new InventoryProblemException(409, "SendAlreadyRequested", "Consulta la solicitud de envío que ya conserva este pedido.");
            await context.Locks.DocumentsAsync([new("supplier", order.SupplierId)], token);
            var ids = order.Lines.Select(value => value.ArticleId).Distinct().ToArray();
            await context.Locks.ArticlesAsync(ids, token);
            var supplier = await db.Set<Supplier>().SingleAsync(value => value.Id == order.SupplierId, token);
            var articles = await db.Articles.Where(value => ids.Contains(value.Id)).ToListAsync(token);
            if (!supplier.IsActive) throw new InventoryProblemException(409, "SupplierInactive", "El proveedor está inactivo; no puede emitirse una compra nueva.");
            if (articles.Any(value => !value.IsActive)) throw new InventoryProblemException(409, "ArticleInactive", "Hay artículos inactivos en el pedido.");
            if (!PurchaseMailOptions.Mailbox(supplier.Email)) throw InventoryProblemException.Validation("supplierEmail", "Completa o corrige el correo del proveedor antes de enviar.");
            var preview = context.Services.GetRequiredService<PurchaseSendReview>().Build(order, supplier, articles);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(reviewToken!), Convert.FromBase64String(preview.ReviewToken)))
                throw new InventoryProblemException(409, "SendReviewChanged", "El documento o contacto cambió desde la revisión. Vuelve a revisarlo antes de confirmar.");
            var now = await PurchaseDeliveryClock.UtcNowAsync(db, token);
            var rendered = await context.Services.GetRequiredService<PurchaseOrderDocumentRenderer>()
                .MessageAsync(preview.Document, preview.RecipientEmail!, preview.SenderEmail, now, token);
            var delivery = PurchaseOrderDelivery.Create(deliveryId, order, context.ActorId, operation, preview.RecipientEmail!, preview.SenderEmail,
                rendered.MessageId, JsonSerializer.Serialize(preview.Document, OperationResponse.JsonOptions), rendered.DocumentBytes, rendered.MimeBytes,
                PurchaseOrderDocumentRenderer.TemplateVersion, preview.ReviewToken, now);
            var before = PurchaseOrderDetail.From(order);
            order.RequestSend();
            // Obtain aggregate rowversion before adding the graph that references the pending OperationResult.
            await db.SaveChangesAsync(token);
            db.Add(delivery); db.Add(OutboxMessage.Create(delivery, now));
            db.Add(new PurchaseDeliveryEvent(delivery.Id, PurchaseDeliveryEventKind.Requested, context.ActorId, operation, operation,
                PurchaseExecutionKind.Human, "{\"sendStatus\":\"Pending\"}"));
            PurchaseOrderAuditWriter.Record(context, id, PurchaseAuditAction.RequestSend, before, PurchaseOrderDetail.From(order, delivery.Id));
            return OperationResponse.Json(202, new PurchaseSendMutation(operation, id, delivery.Id, order.State.ToString(), order.SendStatus.ToString()), id,
                new Dictionary<string, string> { ["ETag"] = EntityTags.Format(order.Version), ["Location"] = $"/api/v1/purchase-orders/{id:D}/delivery" });
        }, cancellation);
    }
}
