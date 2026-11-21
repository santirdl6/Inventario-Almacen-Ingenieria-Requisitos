namespace Inventario.Api.Domain.Stock;

/// <summary>
/// Línea de un conteo (data-model): esperada (creada al iniciar con su teórico) o hallazgo. El contado
/// no lleva motivo; el motivo de cada diferencia se fija solo en la revisión (H6 de analyze-1).
/// <see cref="Difference"/> es una columna calculada persistida; el dominio la mantiene igual en memoria.
/// </summary>
public sealed class CountLine
{
    public const int ReasonMaxLength = 500;

    private CountLine() { }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CountId { get; private set; }
    public Guid LocationId { get; private set; }
    public Guid ArticleId { get; private set; }
    public string LotCode { get; private set; } = "";
    /// <summary>Null solo en un hallazgo de lote nuevo; el ajuste crea el lote (D-13).</summary>
    public Guid? LotId { get; private set; }
    public bool IsFound { get; private set; }
    public decimal TheoreticalPhysical { get; private set; }
    public decimal? CountedPhysical { get; private set; }
    public decimal? Difference { get; private set; }
    public Guid? CountedBy { get; private set; }
    public DateTimeOffset? CountedAt { get; private set; }
    public string? Reason { get; private set; }
    public Guid? ReasonBy { get; private set; }
    /// <summary>Salida confirmada que corrige (D-09); se fija en la revisión (T012).</summary>
    public Guid? CorrectsShipmentId { get; private set; }

    public bool HasDifference => Difference is { } value && value != 0m;

    internal static CountLine Expected(Guid countId, CountExpectedLine expected)
    {
        if (expected.ArticleId == Guid.Empty || expected.LocationId == Guid.Empty || expected.LotId == Guid.Empty)
            throw new ArgumentException("La línea esperada requiere artículo, ubicación y lote.", nameof(expected));
        return new CountLine
        {
            CountId = countId,
            ArticleId = expected.ArticleId,
            LocationId = expected.LocationId,
            LotId = expected.LotId,
            LotCode = RequireLotCode(expected.LotCode),
            IsFound = false,
            TheoreticalPhysical = NonNegative(expected.TheoreticalPhysical, nameof(expected.TheoreticalPhysical))
        };
    }

    internal static CountLine Found(Guid countId, Guid articleId, Guid locationId, Guid? lotId, string lotCode, decimal counted,
        Guid actorId, DateTimeOffset now)
    {
        if (articleId == Guid.Empty || locationId == Guid.Empty || lotId == Guid.Empty)
            throw new ArgumentException("El hallazgo requiere artículo y ubicación.", nameof(articleId));
        if (counted <= 0m)
            throw new ArgumentException("Un hallazgo tiene una cantidad mayor que cero.", nameof(counted));
        var line = new CountLine
        {
            CountId = countId,
            ArticleId = articleId,
            LocationId = locationId,
            LotId = lotId,
            LotCode = RequireLotCode(lotCode),
            IsFound = true,
            TheoreticalPhysical = 0m
        };
        line.Record(counted, actorId, now);
        return line;
    }

    internal void Record(decimal counted, Guid actorId, DateTimeOffset now)
    {
        var value = NonNegative(counted, nameof(counted));
        if (IsFound && value == 0m)
            throw new ArgumentException("Un hallazgo tiene una cantidad mayor que cero; retíralo si no existe.", nameof(counted));
        CountedPhysical = value;
        Difference = value - TheoreticalPhysical;
        CountedBy = actorId;
        CountedAt = now;
    }

    internal void SetReason(string? reason, Guid actorId)
    {
        var normalized = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (normalized is { Length: > ReasonMaxLength })
            throw new ArgumentException($"El motivo no puede superar {ReasonMaxLength} caracteres.", nameof(reason));
        if (normalized is not null && !HasDifference)
            throw new InvalidOperationException("Solo las líneas con diferencia llevan motivo.");
        Reason = normalized;
        ReasonBy = normalized is null ? null : actorId;
    }

    /// <summary>
    /// Vínculo a la salida confirmada que la diferencia corrige (D-09). Limitación P-07 (aprobada por A el
    /// 2026-11-21): solo documenta la corrección del físico; la salida, lo servido y el estado del pedido no
    /// cambian, porque corregirlos exigiría su acumulado y estado en el mismo commit (contrato común).
    /// </summary>
    internal void SetCorrection(Guid? shipmentId)
    {
        if (shipmentId == Guid.Empty) throw new ArgumentException("La salida no es válida.", nameof(shipmentId));
        if (shipmentId is not null && !HasDifference)
            throw new InvalidOperationException("Solo una línea con diferencia corrige una salida.");
        CorrectsShipmentId = shipmentId;
    }

    private static string RequireLotCode(string lotCode) =>
        Lot.NormalizeCode(lotCode) ?? throw new ArgumentException("La línea requiere el código de lote.", nameof(lotCode));

    private static decimal NonNegative(decimal value, string name)
    {
        if (value < 0m || decimal.Round(value, 3) != value)
            throw new ArgumentException("La cantidad es positiva o cero, con tres decimales como máximo.", name);
        return value;
    }
}
