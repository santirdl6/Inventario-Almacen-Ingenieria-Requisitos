using Inventario.Api.Domain.Stock;

namespace Inventario.Api.Domain.MasterData;

public sealed class Article
{
    private Article() { }

    public static Article Create(Guid familyId, string sku, string name, string baseUnit,
        bool allowsFractional, bool tracksLot, Guid? id = null)
    {
        if (familyId == Guid.Empty || id == Guid.Empty) throw new ArgumentException("El artículo requiere identidades válidas.");
        var article = new Article { Id = id ?? Guid.NewGuid(), FamilyId = familyId };
        article.Change(sku, name, familyId, baseUnit, allowsFractional, tracksLot, true);
        article.Policy = new StockPolicy(article.Id);
        return article;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Sku { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string BaseUnit { get; private set; } = "";
    public Guid FamilyId { get; private set; }
    public bool AllowsFractional { get; private set; }
    public bool TracksLot { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool IsDefinitionLocked { get; private set; }
    public StockPolicy Policy { get; private set; } = null!;
    public byte[] Version { get; private set; } = [];

    internal void LockDefinition() => IsDefinitionLocked = true;

    internal void Change(string sku, string name, Guid familyId, string unit, bool fractions, bool lots, bool active)
    {
        var normalizedUnit = RequiredText(unit, 32, nameof(unit)).ToUpperInvariant();
        if (IsDefinitionLocked && (normalizedUnit != BaseUnit || fractions != AllowsFractional || lots != TracksLot))
            throw new InvalidOperationException("La definición ya utilizada no se puede reinterpretar.");
        if (familyId == Guid.Empty) throw new ArgumentException("La familia es obligatoria.");
        Sku = RequiredText(sku, 64, nameof(sku)).ToUpperInvariant();
        Name = RequiredText(name, 200, nameof(name));
        FamilyId = familyId; BaseUnit = normalizedUnit; AllowsFractional = fractions; TracksLot = lots; IsActive = active;
    }

    private static string RequiredText(string value, int maximumLength, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentException($"El valor no puede superar {maximumLength} caracteres.", parameter);
        return normalized;
    }
}
