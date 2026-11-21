using System.Text.Json;
using Inventario.Api.Domain.MasterData;

namespace Inventario.Api.Domain.Purchasing;

public sealed record PurchaseArticleSnapshot(Guid Id, string Sku, string Name, string BaseUnit,
    bool AllowsFractional, bool TracksLot, int SchemaVersion = 1)
{
    public static PurchaseArticleSnapshot From(Article article) =>
        new(article.Id, article.Sku, article.Name, article.BaseUnit, article.AllowsFractional, article.TracksLot);
}

public sealed class PurchaseOrderLine
{
    private PurchaseOrderLine() { }
    public Guid Id { get; private set; }
    public Guid PurchaseOrderId { get; private set; }
    public int Position { get; private set; }
    public Guid ArticleId { get; private set; }
    public string ArticleSnapshot { get; private set; } = "";
    public decimal OrderedQuantity { get; private set; }
    public decimal ReceivedQuantity { get; private set; }
    public decimal PendingQuantity => OrderedQuantity - ReceivedQuantity;
    public decimal UnitPrice { get; private set; }
    public DateOnly ExpectedDeliveryDate { get; private set; }

    internal static PurchaseOrderLine Create(Guid id, Guid orderId, int position, PurchaseArticleSnapshot article,
        decimal quantity, decimal price, DateOnly expected)
    {
        if (id == Guid.Empty || orderId == Guid.Empty || article.Id == Guid.Empty)
            throw new ArgumentException("La línea requiere identidades válidas.");
        if (!article.AllowsFractional && decimal.Truncate(quantity) != quantity)
            throw new ArgumentException("El artículo no admite fracciones.");
        var line = new PurchaseOrderLine { Id = id, PurchaseOrderId = orderId, ArticleId = article.Id,
            ArticleSnapshot = JsonSerializer.Serialize(article, PurchaseSnapshotJson.Options) };
        line.Change(position, quantity, price, expected);
        return line;
    }

    internal void Change(int position, decimal quantity, decimal price, DateOnly expected)
    {
        if (position <= 0 || quantity <= 0 || quantity > 999999999999999.999m
            || decimal.Round(quantity, 3) != quantity || price < 0 || price > 9999999999999999.99m
            || decimal.Round(price, 2) != price || ReceivedQuantity > quantity)
            throw new ArgumentException("Posición, cantidad o precio de línea inválidos.");
        var definition = JsonSerializer.Deserialize<PurchaseArticleSnapshot>(ArticleSnapshot, PurchaseSnapshotJson.Options)!;
        if (!definition.AllowsFractional && decimal.Truncate(quantity) != quantity)
            throw new ArgumentException("La unidad conservada no admite fracciones.");
        Position = position; OrderedQuantity = quantity; UnitPrice = price; ExpectedDeliveryDate = expected;
    }

    internal void StagePosition(int position)
    {
        if (position <= 0) throw new ArgumentOutOfRangeException(nameof(position));
        Position = position;
    }

    internal void SetReceivedQuantity(decimal quantity)
    {
        if (quantity < 0 || quantity > OrderedQuantity || decimal.Round(quantity, 3) != quantity)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        ReceivedQuantity = quantity;
    }
}
