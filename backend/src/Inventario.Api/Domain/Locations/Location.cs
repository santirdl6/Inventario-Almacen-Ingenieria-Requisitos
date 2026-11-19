namespace Inventario.Api.Domain.Locations;

public enum LocationKind
{
    Storage,
    Receiving
}

/// <summary>Valores auditados de una ubicación (MasterDataAudit Before/After, HU-02 FR-015).</summary>
public sealed record LocationSnapshot(string Code, string Aisle, string Rack, string Level, string Kind, bool IsActive);

// Referencia H0 para el núcleo de stock, ampliada por HU-02 (mantenimiento de ubicaciones).
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

    // HU-02: alta con identidad decidida por el comando (estable en reintentos del ejecutor).
    public Location(Guid id, string code, string aisle, string rack, string level, LocationKind kind)
        : this(code, aisle, rack, level, kind)
    {
        if (id == Guid.Empty) throw new ArgumentException("La ubicación requiere un identificador.", nameof(id));
        Id = id;
    }

    /// <summary>
    /// Normaliza como el constructor (recortar, mayúsculas, longitud máxima) y devuelve el
    /// mensaje de error del campo, o null si es válido. Code: máx. 32; Aisle/Rack/Level: máx. 16.
    /// </summary>
    public static string? Normalize(string? value, int maximumLength, out string normalized)
    {
        normalized = value?.Trim().ToUpperInvariant() ?? "";
        if (normalized.Length == 0) return "Este dato es obligatorio y no puede quedar en blanco.";
        return normalized.Length > maximumLength ? $"No puede superar {maximumLength} caracteres." : null;
    }

    /// <summary>Corrige la identificación física. Las reglas que dependen de saldos (FR-011)
    /// las aplica el comando bajo location:{id}. Devuelve si hubo cambio efectivo.</summary>
    public bool Correct(string code, string aisle, string rack, string level)
    {
        var next = new Location(code, aisle, rack, level, Kind);
        if (next.Code == Code && next.Aisle == Aisle && next.Rack == Rack && next.Level == Level) return false;
        (Code, Aisle, Rack, Level) = (next.Code, next.Aisle, next.Rack, next.Level);
        return true;
    }

    public bool ChangeKind(LocationKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (Kind == kind) return false;
        Kind = kind;
        return true;
    }

    public bool Deactivate()
    {
        if (!IsActive) return false;
        IsActive = false;
        return true;
    }

    public bool Reactivate()
    {
        if (IsActive) return false;
        IsActive = true;
        return true;
    }

    public LocationSnapshot AuditSnapshot() => new(Code, Aisle, Rack, Level, Kind.ToString(), IsActive);

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
