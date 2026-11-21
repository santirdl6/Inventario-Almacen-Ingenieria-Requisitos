using System.Globalization;
using System.Text.Json;
using Inventario.Api.Domain.Purchasing;

namespace Inventario.Api.Features.Purchasing;

public sealed record PurchaseLineDto(Guid Id, int Position, Guid ArticleId, PurchaseArticleSnapshot ArticleSnapshot,
    string OrderedQuantity, string ReceivedQuantity, string PendingQuantity, string UnitPrice, DateOnly ExpectedDeliveryDate);

public sealed record PurchaseOrderDetail(Guid Id, string Number, Guid SupplierId, PurchaseSupplierSnapshot SupplierSnapshot,
    string Currency, DateOnly ExpectedDeliveryDate, string State, string SendStatus, Guid? DeliveryId,
    Guid CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? SentAt, IReadOnlyList<PurchaseLineDto> Lines)
{
    public static PurchaseOrderDetail From(PurchaseOrder order, Guid? deliveryId = null) => new(order.Id, order.Number,
        order.SupplierId, JsonSerializer.Deserialize<PurchaseSupplierSnapshot>(order.SupplierSnapshot, PurchaseSnapshotJson.Options)!,
        order.Currency, order.ExpectedDeliveryDate, order.State.ToString(), order.SendStatus.ToString(), deliveryId,
        order.CreatedBy, order.CreatedAt, order.SentAt, order.Lines.OrderBy(line => line.Position).Select(line => new PurchaseLineDto(
            line.Id, line.Position, line.ArticleId, JsonSerializer.Deserialize<PurchaseArticleSnapshot>(line.ArticleSnapshot, PurchaseSnapshotJson.Options)!,
            line.OrderedQuantity.ToString("F3", CultureInfo.InvariantCulture), line.ReceivedQuantity.ToString("F3", CultureInfo.InvariantCulture),
            line.PendingQuantity.ToString("F3", CultureInfo.InvariantCulture), line.UnitPrice.ToString("F2", CultureInfo.InvariantCulture),
            line.ExpectedDeliveryDate)).ToArray());
}

public sealed record PurchaseOrderMutation(Guid OperationId, PurchaseOrderDetail Resource);

public sealed record PurchaseOrderSummary(Guid Id, string Number, Guid SupplierId, PurchaseSupplierSnapshot SupplierSnapshot,
    DateOnly ExpectedDeliveryDate, DateTimeOffset CreatedAt, string State, string SendStatus)
{
    public static PurchaseOrderSummary From(PurchaseOrder order) => new(order.Id, order.Number, order.SupplierId,
        JsonSerializer.Deserialize<PurchaseSupplierSnapshot>(order.SupplierSnapshot, PurchaseSnapshotJson.Options)!,
        order.ExpectedDeliveryDate, order.CreatedAt, order.State.ToString(), order.SendStatus.ToString());
}
public sealed record PurchaseOrderPage(IReadOnlyList<PurchaseOrderSummary> Items, int Page, int PageSize, int TotalCount);
