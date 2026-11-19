namespace Inventario.Api.Domain.Locations;

public enum LocationKind
{
    Storage,
    Receiving
}

// Referencia mínima H0 para el núcleo de stock. El mantenimiento completo se implementa en HU-02.
public sealed class Location
{
    public const int CodeMaxLength = 32;
    public const int PartMaxLength = 16;

    private Location() { }

    public Location(string code, string aisle, string rack, string level, LocationKind kind)
    {
        Code = RequiredText(code, CodeMaxLength, nameof(code)).ToUpperInvariant();
        Aisle = RequiredText(aisle, PartMaxLength, nameof(aisle)).ToUpperInvariant();
        Rack = RequiredText(rack, PartMaxLength, nameof(rack)).ToUpperInvariant();
        Level = RequiredText(level, PartMaxLength, nameof(level)).ToUpperInvariant();
        Kind = kind;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Code { get; private set; } = "";
    public string Aisle { get; private set; } = "";
    public string Rack { get; private set; } = "";
    public string Level { get; private set; } = "";
    public LocationKind Kind { get; private set; }
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
