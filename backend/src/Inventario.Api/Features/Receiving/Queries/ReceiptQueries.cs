using System.Data;
using System.Text.Json;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Domain.Receiving;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Receiving.Queries;

public sealed class ReceiptQueries(InventoryDbContext db)
{
    public Task<object> ListAsync(Guid? purchaseOrderId, Guid? articleId, Guid? actorId, string? from, string? to,
        string? state, int page, int pageSize, CancellationToken token) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw ReceiptWrite.Invalid("page", "Indica una página válida (1–100 filas).");
        var start = from is null ? (DateTimeOffset?)null : ReceiptWrite.ParseDate(from, "from");
        var end = to is null ? (DateTimeOffset?)null : ReceiptWrite.ParseDate(to, "to");
        if (start >= end) throw ReceiptWrite.Invalid("to", "El fin debe ser posterior al inicio.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot, token);
        var query = db.Set<Receipt>().AsNoTracking();
        if (purchaseOrderId is Guid order) query = query.Where(value => value.PurchaseOrderId == order);
        if (actorId is Guid actor) query = query.Where(value => (value.ActorId ?? value.CreatedBy) == actor);
        if (articleId is Guid article) query = query.Where(value => value.Lines.Any(line =>
            db.Set<PurchaseOrderLine>().Any(purchase => purchase.Id == line.PurchaseOrderLineId && purchase.ArticleId == article)));
        if (start is not null) query = query.Where(value => value.OccurredAt >= start);
        if (end is not null) query = query.Where(value => value.OccurredAt < end);
        if (state is not null)
        {
            if (!Enum.TryParse<ReceiptState>(state, out var selected) || selected.ToString() != state)
                throw ReceiptWrite.Invalid("state", "Selecciona Draft o Confirmed.");
            query = query.Where(value => value.State == selected);
        }
        var totalCount = await query.CountAsync(token);
        var items = await query.OrderByDescending(value => value.OccurredAt).ThenBy(value => value.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(value => new { value.Id, value.PurchaseOrderId, state = value.State.ToString(), value.OccurredAt, value.ActorId, value.SupplierDocumentReference })
            .ToListAsync(token);
        await tx.CommitAsync(token);
        return (object)new { items, page, pageSize, totalCount };
    });

    public Task<(object Resource, string Tag)> DetailAsync(Guid id, CancellationToken token) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot, token);
        var receipt = await DocumentAsync(id, token);
        var corrections = await db.Set<ReceiptCorrection>().AsNoTracking().Where(value => value.ReceiptId == id)
            .OrderBy(value => value.RecordedAt).ThenBy(value => value.Id).ToArrayAsync(token);
        var correctionIds = corrections.Select(value => value.Id).ToArray();
        var lines = await db.Set<ReceiptCorrectionLine>().AsNoTracking().Where(value => correctionIds.Contains(value.CorrectionId)).ToArrayAsync(token);
        var evidence = corrections.Select(value => (object)new { value.Id, value.OriginalMovementId, value.MovementId,
            value.ActorId, value.OperationId, value.RecordedAt, value.Reason, value.Reference,
            lines = lines.Where(line => line.CorrectionId == value.Id).OrderBy(line => line.Id)
                .Select(line => new { line.ReceiptAllocationId, quantity = ReceiptDocument.Amount(line.Quantity) }).ToArray() }).ToArray();
        var result = (ReceiptDocument.Resource(receipt, evidence), ReceiptDocument.Tag(receipt));
        await tx.CommitAsync(token);
        return result;
    });

    public Task<object> ContextAsync(Guid id, CancellationToken token) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot, token);
        var receipt = await DocumentAsync(id, token);
        var order = await db.Set<PurchaseOrder>().AsNoTracking().Include(value => value.Lines)
            .SingleAsync(value => value.Id == receipt.PurchaseOrderId, token);
        var locations = receipt.Lines.SelectMany(line => line.Allocations).Select(item => item.LocationId).Distinct().ToArray();
        var destinations = await db.Set<Location>().AsNoTracking().Where(value => locations.Contains(value.Id))
            .Select(value => new { value.Id, value.Code, value.Aisle, value.Rack, value.Level, value.IsActive }).ToArrayAsync(token);
        var articleIds = order.Lines.Select(line => line.ArticleId).Distinct().ToArray();
        var articles = await db.Articles.AsNoTracking().Where(value => articleIds.Contains(value.Id)).ToDictionaryAsync(value => value.Id, token);
        var lines = order.Lines.OrderBy(line => line.Position).Select(line =>
        {
            var article = articles[line.ArticleId];
            var draft = receipt.Lines.SingleOrDefault(value => value.PurchaseOrderLineId == line.Id);
            return new { purchaseOrderLineId = line.Id, line.ArticleId, article.Sku, article.Name, article.BaseUnit,
                article.TracksLot, article.IsActive, article.AllowsFractional, orderedQuantity = ReceiptDocument.Amount(line.OrderedQuantity),
                receivedQuantity = ReceiptDocument.Amount(line.ReceivedQuantity), pendingQuantity = ReceiptDocument.Amount(line.PendingQuantity),
                projectedPendingQuantity = receipt.State == ReceiptState.Draft ? ReceiptDocument.Amount(line.PendingQuantity - (draft?.AcceptedQuantity ?? 0)) : null };
        }).ToArray();
        var result = new { receiptVersion = ReceiptDocument.Tag(receipt), purchaseOrderVersion = EntityTags.Format(order.Version),
            orderNumber = order.Number, purchaseState = order.State.ToString(), sendStatus = order.SendStatus.ToString(),
            supplier = JsonSerializer.Deserialize<PurchaseSupplierSnapshot>(order.SupplierSnapshot, OperationResponse.JsonOptions), lines, destinations };
        await tx.CommitAsync(token);
        return (object)result;
    });

    private async Task<Receipt> DocumentAsync(Guid id, CancellationToken token) =>
        await ReceiptDocument.Graph(db).AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, token)
        ?? throw new InventoryProblemException(404, "NotFound", "La recepción no existe.");
}
