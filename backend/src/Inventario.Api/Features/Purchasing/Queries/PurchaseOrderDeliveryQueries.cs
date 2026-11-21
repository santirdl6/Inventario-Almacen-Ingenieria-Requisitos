using System.Data;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Purchasing.Queries;

public sealed record PurchaseDeliveryStatus(Guid PurchaseOrderId, string PurchaseOrderVersion, Guid? DeliveryId, string? MessageId,
    string? RecipientEmail, string? SenderEmail,
    string SendStatus, int Attempts, int CycleAttempts, DateTimeOffset? RequestedAt, DateTimeOffset? LastAttemptAt,
    DateTimeOffset? NextAttemptAt, DateTimeOffset? AcceptedAt, string? LastErrorCode, string? LastErrorSummary, bool AcceptanceUncertain, bool CanRetry);

public sealed class PurchaseOrderDeliveryQueries(InventoryDbContext database)
{
    public Task<PurchaseDeliveryStatus> ReadAsync(Guid id, bool canSend, CancellationToken cancellation) => database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        await using var tx = await database.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
        var order = await database.Set<PurchaseOrder>().AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellation)
            ?? throw new InventoryProblemException(404, "NotFound", "El pedido no existe.");
        var delivery = await database.Set<PurchaseOrderDelivery>().AsNoTracking().SingleOrDefaultAsync(value => value.PurchaseOrderId == id, cancellation);
        var outbox = await database.Set<OutboxMessage>().AsNoTracking().SingleOrDefaultAsync(value => value.DocumentId == id, cancellation);
        if ((delivery is null) != (outbox is null)) throw new InventoryProblemException(503, "DependencyUnavailable", "No se pudo comprobar la entrega completa del pedido.");
        DateTimeOffset? last = null;
        if (delivery is not null) last = await database.Set<PurchaseDeliveryEvent>().AsNoTracking()
            .Where(value => value.DeliveryId == delivery.Id && value.Kind == PurchaseDeliveryEventKind.Claimed)
            .OrderByDescending(value => value.RecordedAt).Select(value => (DateTimeOffset?)value.RecordedAt).FirstOrDefaultAsync(cancellation);
        var result = new PurchaseDeliveryStatus(id, EntityTags.Format(order.Version), delivery?.Id, delivery?.MessageId, delivery?.RecipientEmail, delivery?.SenderEmail,
            order.SendStatus.ToString(), outbox?.Attempts ?? 0, outbox?.CycleAttempts ?? 0, delivery?.CreatedAt, last,
            outbox?.State == PurchaseOutboxState.Pending ? outbox.NextAttemptAt : null, order.SentAt,
            outbox?.LastErrorCode, outbox?.LastErrorSummary, outbox?.AcceptanceUncertain ?? false,
            canSend && outbox?.State == PurchaseOutboxState.Failed);
        await tx.CommitAsync(cancellation); return result;
    });

    public async Task<(byte[] Bytes, string MediaType, string Name)> DocumentAsync(Guid id, CancellationToken cancellation)
    {
        var delivery = await database.Set<PurchaseOrderDelivery>().AsNoTracking().SingleOrDefaultAsync(value => value.PurchaseOrderId == id, cancellation)
            ?? throw new InventoryProblemException(404, "NotFound", "El pedido todavía no tiene documento de envío conservado.");
        return (delivery.DocumentBytes, delivery.MediaType, delivery.DocumentName);
    }
}
