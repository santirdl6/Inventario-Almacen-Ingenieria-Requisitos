using System.Data;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Purchasing.Queries;

public sealed class PurchaseOrderSendPreviewQuery(InventoryDbContext database, PurchaseSendReview review)
{
    public Task<PurchaseSendPreview> ReadAsync(Guid id, CancellationToken cancellation) => database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        await using var tx = await database.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
        var order = await database.Set<PurchaseOrder>().AsNoTracking().Include(value => value.Lines).SingleOrDefaultAsync(value => value.Id == id, cancellation)
            ?? throw new InventoryProblemException(404, "NotFound", "El pedido no existe.");
        if (!order.IsEditable) throw new InventoryProblemException(409, "SendAlreadyRequested", "Consulta el envío conservado de este pedido.");
        var supplier = await database.Set<Supplier>().AsNoTracking().SingleAsync(value => value.Id == order.SupplierId, cancellation);
        var ids = order.Lines.Select(value => value.ArticleId).Distinct().ToArray();
        var articles = await database.Articles.AsNoTracking().Where(value => ids.Contains(value.Id)).ToListAsync(cancellation);
        var result = review.Build(order, supplier, articles);
        await tx.CommitAsync(cancellation); return result;
    });
}
