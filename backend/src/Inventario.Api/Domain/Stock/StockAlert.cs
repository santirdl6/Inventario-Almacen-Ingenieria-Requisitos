namespace Inventario.Api.Domain.Stock;

public sealed class StockAlert
{
    private StockAlert() { }

    public StockAlert(Guid articleId, decimal physical, decimal reserved, decimal minimum, DateTimeOffset now)
    {
        if (articleId == Guid.Empty) throw new ArgumentException("La alerta requiere un artículo.");
        Id = Guid.NewGuid();
        ArticleId = articleId;
        State = "Active";
        OpenedAt = now.ToUniversalTime();
        Update(physical, reserved, minimum, now);
    }

    public Guid Id { get; private set; }
    public Guid ArticleId { get; private set; }
    public string State { get; private set; } = "Active";
    public decimal PhysicalSnapshot { get; private set; }
    public decimal ReservedSnapshot { get; private set; }
    public decimal AvailableSnapshot { get; private set; }
    public decimal MinimumSnapshot { get; private set; }
    public decimal SuggestedQuantity { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    public void Update(decimal physical, decimal reserved, decimal minimum, DateTimeOffset now)
    {
        if (State != "Active" || physical < 0 || reserved < 0 || reserved > physical || minimum < 0)
            throw new InvalidOperationException("Los datos de la alerta no son válidos.");
        PhysicalSnapshot = physical;
        ReservedSnapshot = reserved;
        AvailableSnapshot = physical - reserved;
        MinimumSnapshot = minimum;
        SuggestedQuantity = Math.Max(0m, minimum - AvailableSnapshot);
        UpdatedAt = now.ToUniversalTime();
    }

    public void Resolve(DateTimeOffset now)
    {
        if (State != "Active") throw new InvalidOperationException("La alerta ya está resuelta.");
        State = "Resolved";
        UpdatedAt = now.ToUniversalTime();
        ResolvedAt = UpdatedAt;
    }
}
