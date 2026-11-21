using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventario.Api.Features.Purchasing.Commands;

public sealed class PurchaseOrderDraftCommands(InventoryOperationExecutor executor)
{
    public Task<OperationResponse> WriteAsync(Guid operation, Guid? id, PurchaseOrderWrite write, string? ifMatch, CancellationToken cancellation)
    {
        var orderId = id ?? Guid.NewGuid();
        return executor.ExecuteAsync(new(operation, id is null ? "POST" : "PUT",
            id is null ? "/api/v1/purchase-orders" : $"/api/v1/purchase-orders/{orderId:D}", "Purchasing.Manage",
            JsonSerializer.SerializeToElement(write, OperationResponse.JsonOptions)), async (context, token) =>
        {
            var db = context.Database;
            await context.Locks.DocumentsAsync([new("purchase-order", orderId)], token);
            var order = id is null ? null : await db.Set<PurchaseOrder>().Include(value => value.Lines)
                .SingleOrDefaultAsync(value => value.Id == orderId, token);
            if (id is not null && order is null) throw new InventoryProblemException(404, "NotFound", "El pedido no existe.");
            if (order is not null)
            {
                EntityTags.RequireMatch(ifMatch, order.Version);
                if (!order.IsEditable) throw new InventoryProblemException(409, "PurchaseOrderNotEditable", "El pedido quedó congelado al solicitar envío.");
            }
            // purchase-order sorts before supplier; discovery is stable under the order lock.
            var suppliers = new[] { write.SupplierId, order?.SupplierId ?? write.SupplierId }.Distinct();
            await context.Locks.DocumentsAsync(suppliers.Select(value => new DocumentLock("supplier", value)), token);
            var oldLines = order?.Lines.ToArray() ?? [];
            var articleIds = write.Lines.Select(value => value.ArticleId).Concat(oldLines.Select(value => value.ArticleId)).Distinct().ToArray();
            await context.Locks.ArticlesAsync(articleIds, token);
            var supplier = await db.Set<Supplier>().SingleOrDefaultAsync(value => value.Id == write.SupplierId, token)
                ?? throw InventoryProblemException.Validation("supplierId", "El proveedor no existe.");
            if (!supplier.IsActive && (order is null || order.SupplierId != supplier.Id))
                throw new InventoryProblemException(409, "SupplierInactive", "El proveedor está inactivo y no admite nuevas compras.");
            var articles = await db.Articles.Where(value => articleIds.Contains(value.Id)).ToDictionaryAsync(value => value.Id, token);
            var requestedIds = write.Lines.Select(value => value.Id).ToArray();
            if (await db.Set<PurchaseOrderLine>().AnyAsync(value => requestedIds.Contains(value.Id) && value.PurchaseOrderId != orderId, token))
                throw InventoryProblemException.Validation("lines", "Una identidad de línea ya pertenece a otro pedido.");
            var errors = new Dictionary<string, string[]>();
            for (var index = 0; index < write.Lines.Length; index++)
            {
                var value = write.Lines[index]; var old = oldLines.SingleOrDefault(line => line.Id == value.Id);
                if (!articles.TryGetValue(value.ArticleId, out var article)) errors[$"lines[{index}].articleId"] = ["El artículo no existe."];
                else
                {
                    if (old is not null && old.ArticleId != value.ArticleId) errors[$"lines[{index}].id"] = ["Cambiar de artículo requiere otra identidad de línea."];
                    if (!article.IsActive && old is null) throw new InventoryProblemException(409, "ArticleInactive", $"El artículo {article.Sku} está inactivo.");
                    if (!article.AllowsFractional && decimal.Truncate(value.Amount) != value.Amount)
                        errors[$"lines[{index}].quantity"] = [$"El artículo se compra en unidades enteras de {article.BaseUnit}."];
                }
            }
            if (errors.Count > 0) throw new InventoryProblemException(422, "ValidationFailed", "Corrige las líneas indicadas del pedido.", errors);
            var before = order is null ? null : PurchaseOrderDetail.From(order);
            // Stage retained positions out of the final range so swaps cannot violate the unique index.
            if (oldLines.Length != 0 && !oldLines.OrderBy(line => line.Position).Select(line => line.Id).SequenceEqual(requestedIds))
            {
                var offset = checked(oldLines.Max(line => line.Position) + write.Lines.Length);
                foreach (var line in oldLines) line.StagePosition(checked(offset + line.Position));
                await db.SaveChangesAsync(token);
            }
            var content = write.Lines.Select((value, index) =>
            {
                var existing = oldLines.SingleOrDefault(line => line.Id == value.Id);
                if (existing is null) return PurchaseOrderLine.Create(value.Id, orderId, index + 1,
                    PurchaseArticleSnapshot.From(articles[value.ArticleId]), value.Amount, value.Price, value.ExpectedDeliveryDate);
                existing.Change(index + 1, value.Amount, value.Price, value.ExpectedDeliveryDate); return existing;
            }).ToArray();
            var snapshot = PurchaseSupplierSnapshot.From(supplier);
            if (order is null)
            {
                order = PurchaseOrder.Create(orderId, snapshot, context.Services.GetRequiredService<IOptions<PurchasingOptions>>().Value.Currency,
                    write.ExpectedDeliveryDate, context.ActorId, content);
                db.Add(order);
            }
            else
            {
                db.RemoveRange(oldLines.Where(line => !requestedIds.Contains(line.Id)));
                order.Edit(snapshot, write.ExpectedDeliveryDate, content);
                foreach (var line in content.Where(line => !oldLines.Contains(line))) db.Add(line);
            }
            await context.Services.GetRequiredService<ArticleReferenceTracker>().MarkAsync(context, content.Select(value => value.ArticleId), token);
            // No result-referencing evidence has been added yet: flush only the aggregate and definition marker.
            await db.SaveChangesAsync(token);
            var after = PurchaseOrderDetail.From(order);
            PurchaseOrderAuditWriter.Record(context, order.Id, before is null ? PurchaseAuditAction.Create : PurchaseAuditAction.Update, before, after);
            var headers = new Dictionary<string, string> { ["ETag"] = EntityTags.Format(order.Version) };
            if (id is null) headers["Location"] = $"/api/v1/purchase-orders/{order.Id:D}";
            return OperationResponse.Json(id is null ? 201 : 200, new PurchaseOrderMutation(operation, after), order.Id, headers);
        }, cancellation);
    }
}
