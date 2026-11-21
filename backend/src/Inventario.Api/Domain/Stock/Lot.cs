namespace Inventario.Api.Domain.Stock;

// Bucket de lote de un artículo. Los artículos sin seguimiento usan un único lote SIN-LOTE propio.
public sealed class Lot
{
    public const string UntrackedCode = "SIN-LOTE";
    public const int CodeMaxLength = 64;

    private Lot() { }

    private Lot(Guid articleId, string code, bool isUntracked)
    {
        ArticleId = articleId;
        Code = code;
        IsUntracked = isUntracked;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ArticleId { get; private set; }
    public string Code { get; private set; } = "";
    public bool IsUntracked { get; private set; }

    public static Lot Untracked(Guid articleId) => new(articleId, UntrackedCode, true);

    public static Lot Tracked(Guid articleId, string code)
    {
        var normalized = NormalizeCode(code)
            ?? throw new ArgumentException("El lote es obligatorio.", nameof(code));
        if (normalized == UntrackedCode)
            throw new ArgumentException("SIN-LOTE no es un código válido para un artículo con seguimiento.", nameof(code));
        return new Lot(articleId, normalized, false);
    }

    public static string? NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;
        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length > CodeMaxLength)
            throw new ArgumentException($"El lote no puede superar {CodeMaxLength} caracteres.", nameof(code));
        return normalized;
    }
}
