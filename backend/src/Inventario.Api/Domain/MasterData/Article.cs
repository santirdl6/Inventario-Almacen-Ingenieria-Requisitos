namespace Inventario.Api.Domain.MasterData;

// Base de referencia H0. El mantenimiento y la familia se completan en HU-08.
public sealed class Article
{
    private Article() { }

    public Article(string sku, string name, string baseUnit, bool allowsFractional, bool tracksLot)
    {
        Sku = RequiredText(sku, 64, nameof(sku)).ToUpperInvariant();
        Name = RequiredText(name, 200, nameof(name));
        BaseUnit = RequiredText(baseUnit, 32, nameof(baseUnit)).ToUpperInvariant();
        AllowsFractional = allowsFractional;
        TracksLot = tracksLot;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Sku { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string BaseUnit { get; private set; } = "";
    public bool AllowsFractional { get; private set; }
    public bool TracksLot { get; private set; }
    public bool IsActive { get; private set; } = true;
    public byte[] Version { get; private set; } = [];

    private static string RequiredText(string value, int maximumLength, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentException($"El valor no puede superar {maximumLength} caracteres.", parameter);
        return normalized;
    }
}
