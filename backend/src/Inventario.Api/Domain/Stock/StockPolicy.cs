namespace Inventario.Api.Domain.Stock;

public sealed class StockPolicy
{
    private StockPolicy() { }
    public StockPolicy(Guid articleId)
    {
        if (articleId == Guid.Empty) throw new ArgumentException("La política requiere un artículo.");
        ArticleId = articleId;
    }
    public Guid ArticleId { get; private set; }
    public decimal MinimumAvailable { get; private set; }
    public byte[] Version { get; private set; } = [];

    public void SetMinimum(decimal value)
    {
        if (value < 0 || value >= 1_000_000_000_000_000m || decimal.Round(value, 3) != value)
            throw new ArgumentOutOfRangeException(nameof(value));
        MinimumAvailable = value;
    }
}
