namespace Inventario.Api.Domain.Stock;

// Saldo por bucket artículo/ubicación/lote. Nace en cero y solo cambia mediante el núcleo de stock.
public sealed class StockBalance
{
    private StockBalance() { }

    internal StockBalance(Guid articleId, Guid locationId, Guid lotId)
    {
        ArticleId = articleId;
        LocationId = locationId;
        LotId = lotId;
    }

    public Guid ArticleId { get; private set; }
    public Guid LocationId { get; private set; }
    public Guid LotId { get; private set; }
    public decimal Physical { get; private set; }
    public decimal Reserved { get; private set; }
    public decimal Available => Physical - Reserved;
    public byte[] Version { get; private set; } = [];

    internal void Apply(decimal physicalDelta, decimal reservedDelta)
    {
        var physical = Physical + physicalDelta;
        var reserved = Reserved + reservedDelta;
        if (physical < 0 || reserved < 0 || reserved > physical)
            throw new InvalidOperationException("El movimiento rompería las invariantes del saldo.");
        Physical = physical;
        Reserved = reserved;
    }
}
