using System.Text.Json;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Messaging;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Purchasing.Commands;

public sealed class RetryPurchaseOrderDelivery(InventoryOperationExecutor executor)
{
    public Task<OperationResponse> ExecuteAsync(Guid operation, Guid id, JsonElement body, string? ifMatch, CancellationToken cancellation)
    {
        foreach (var value in body.EnumerateObject())
            if (value.Name is not ("kind" or "deliveryId")) throw InventoryProblemException.Validation(value.Name, "Un reintento conserva documento y destinatario originales.");
        if (!body.TryGetProperty("deliveryId", out var requested) || requested.ValueKind != JsonValueKind.String
            || !requested.TryGetGuid(out var deliveryId) || deliveryId == Guid.Empty)
            throw InventoryProblemException.Validation("deliveryId", "Indica la entrega conservada que deseas reintentar.");
        return executor.ExecuteAsync(new(operation, "POST", $"/api/v1/purchase-orders/{id:D}/send", "Purchasing.Send",
            JsonSerializer.SerializeToElement(new { kind = "retry", deliveryId }, OperationResponse.JsonOptions)), async (context, token) =>
        {
            var db = context.Database;
            await context.Locks.DocumentsAsync([new("purchase-order", id), new("purchase-order-delivery", deliveryId)], token);
            var order = await db.Set<PurchaseOrder>().Include(value => value.Lines).SingleOrDefaultAsync(value => value.Id == id, token)
                ?? throw new InventoryProblemException(404, "NotFound", "El pedido no existe.");
            EntityTags.RequireMatch(ifMatch, order.Version);
            var delivery = await db.Set<PurchaseOrderDelivery>().AsNoTracking().SingleOrDefaultAsync(value => value.Id == deliveryId && value.PurchaseOrderId == id, token);
            var outbox = await db.Set<OutboxMessage>().SingleOrDefaultAsync(value => value.DeliveryId == deliveryId && value.DocumentId == id, token);
            if (delivery is null || outbox is null || outbox.State != PurchaseOutboxState.Failed
                || order.State != PurchaseOrderState.Draft || order.SendStatus != PurchaseSendStatus.Failed)
                throw new InventoryProblemException(409, "DeliveryNotRetryable", "Solo una entrega en error confirmado admite reintento. Comprueba su estado actual.");
            var before = PurchaseOrderDetail.From(order, deliveryId);
            var now = await PurchaseDeliveryClock.UtcNowAsync(db, token);
            order.RetrySend();
            // Outbox authorization is changed only after this flush: its new result FK does not exist yet.
            await db.SaveChangesAsync(token);
            outbox.Retry(context.ActorId, operation, now);
            db.Add(new PurchaseDeliveryEvent(deliveryId, PurchaseDeliveryEventKind.Retried, context.ActorId, operation, operation,
                PurchaseExecutionKind.Human, JsonSerializer.Serialize(new { outbox.Cycle, sendStatus = "Pending" }, OperationResponse.JsonOptions)));
            PurchaseOrderAuditWriter.Record(context, id, PurchaseAuditAction.RetrySend, before, PurchaseOrderDetail.From(order, deliveryId));
            return OperationResponse.Json(202, new PurchaseSendMutation(operation, id, deliveryId, order.State.ToString(), order.SendStatus.ToString()), id,
                new Dictionary<string, string> { ["ETag"] = EntityTags.Format(order.Version), ["Location"] = $"/api/v1/purchase-orders/{id:D}/delivery" });
        }, cancellation);
    }
}
