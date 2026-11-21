namespace Inventario.Api.Domain.Stock;

public enum CustomerOrderState
{
    Open,
    Fulfilled,
    // Definido en el modelo común; no alcanzable en HU-05 (clarificación 3, sin edición ni cancelación).
    Cancelled
}

/// <summary>Línea pedida en el alta: artículo y cantidad en la unidad base del artículo.</summary>
public sealed record CustomerOrderLineRequest(Guid ArticleId, decimal Quantity);

/// <summary>
/// Pedido de cliente mínimo (HU-05): referencia externa única, referencia del cliente y líneas.
/// Sin precios ni datos comerciales. Solo aplica reglas locales; la unicidad de la referencia, la
/// existencia y el estado del artículo y la admisión de fracciones las decide el comando bajo
/// bloqueo (research D-10). Quién y cuándo lo creó constan en su OperationResult.
/// </summary>
public sealed class CustomerOrder
{
    public const int ExternalReferenceMaxLength = 64;
    public const int CustomerReferenceMaxLength = 200;
    public const int MaxLines = 100;

    private readonly List<CustomerOrderLine> lines = [];

    private CustomerOrder() { }

    public Guid Id { get; private set; }
    public string ExternalReference { get; private set; } = "";
    public string CustomerReference { get; private set; } = "";
    public CustomerOrderState State { get; private set; }
    // rowversion: ETag del pedido. Todo escritor de reservas o servido lo renueva marcando State
    // como modificado en su contexto (research D-07), como hacen los comandos de A.
    public byte[] Version { get; private set; } = [];
    public IReadOnlyList<CustomerOrderLine> Lines => lines;

    /// <summary>Alta con identidad decidida por el comando (estable en los reintentos del ejecutor).</summary>
    public static CustomerOrder Create(Guid id, string externalReference, string customerReference,
        IReadOnlyCollection<CustomerOrderLineRequest> lines)
    {
        if (id == Guid.Empty) throw new ArgumentException("El pedido requiere un identificador.", nameof(id));
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
            throw new ArgumentException("El pedido necesita al menos una línea.", nameof(lines));
        if (lines.Count > MaxLines)
            throw new ArgumentException($"El pedido admite como máximo {MaxLines} líneas.", nameof(lines));
        if (lines.GroupBy(line => line.ArticleId).Any(group => group.Count() > 1))
            throw new ArgumentException("Cada artículo solo puede aparecer en una línea del pedido.", nameof(lines));

        var order = new CustomerOrder
        {
            Id = id,
            ExternalReference = RequiredText(externalReference, ExternalReferenceMaxLength, nameof(externalReference)),
            CustomerReference = RequiredText(customerReference, CustomerReferenceMaxLength, nameof(customerReference)),
            State = CustomerOrderState.Open
        };
        foreach (var line in lines)
            order.lines.Add(new CustomerOrderLine(order.Id, line.ArticleId, line.Quantity));
        return order;
    }

    /// <summary>
    /// HU-03 (research D-04): Open → Fulfilled cuando todas las líneas quedan sin pendiente. Un pedido
    /// servido sigue servido; Cancelled no es alcanzable en esta entrega y no se toca.
    /// </summary>
    public void DeriveFulfilment()
    {
        if (State == CustomerOrderState.Open && lines.All(line => line.PendingQuantity == 0))
            State = CustomerOrderState.Fulfilled;
    }

    internal static string RequiredText(string? value, int maximumLength, string field)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length == 0) throw new ArgumentException("Este dato es obligatorio.", field);
        if (trimmed.Length > maximumLength)
            throw new ArgumentException($"No puede superar {maximumLength} caracteres.", field);
        return trimmed;
    }

    /// <summary>Cantidad positiva con como máximo tres decimales (decimal(18,3) común).</summary>
    internal static decimal PositiveQuantity(decimal quantity, string field)
    {
        if (quantity <= 0) throw new ArgumentException("La cantidad debe ser mayor que cero.", field);
        if (decimal.Round(quantity, 3) != quantity)
            throw new ArgumentException("La cantidad admite como máximo tres decimales.", field);
        return quantity;
    }
}

/// <summary>Línea del pedido: un artículo distinto por línea (P-01). Lo servido solo lo cambia HU-03.</summary>
public sealed class CustomerOrderLine
{
    private CustomerOrderLine() { }

    internal CustomerOrderLine(Guid customerOrderId, Guid articleId, decimal orderedQuantity)
    {
        if (articleId == Guid.Empty) throw new ArgumentException("La línea requiere un artículo.", nameof(articleId));
        CustomerOrderId = customerOrderId;
        ArticleId = articleId;
        OrderedQuantity = CustomerOrder.PositiveQuantity(orderedQuantity, nameof(orderedQuantity));
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CustomerOrderId { get; private set; }
    public Guid ArticleId { get; private set; }
    public decimal OrderedQuantity { get; private set; }
    public decimal ShippedQuantity { get; private set; }

    /// <summary>Pendiente = pedido − servido; se calcula, no se almacena.</summary>
    public decimal PendingQuantity => OrderedQuantity - ShippedQuantity;

    /// <summary>HU-03 (research D-04): suma lo servido por una salida confirmada, sin superar lo pedido.</summary>
    public void Ship(decimal quantity)
    {
        CustomerOrder.PositiveQuantity(quantity, nameof(quantity));
        if (quantity > PendingQuantity)
            throw new InvalidOperationException("No se puede servir más que el pendiente de la línea.");
        ShippedQuantity += quantity;
    }
}
