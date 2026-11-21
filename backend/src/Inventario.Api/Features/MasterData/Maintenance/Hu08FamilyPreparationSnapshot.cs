namespace Inventario.Api.Features.MasterData.Maintenance;

public sealed record Hu08FamilyPreparationSnapshot
{
    public Guid Id { get; init; }
    public string Sku { get; init; } = "";
    public string Name { get; init; } = "";
    public Guid? FamilyId { get; init; }
    public string BaseUnit { get; init; } = "";
    public bool AllowsFractional { get; init; }
    public bool TracksLot { get; init; }
    public bool IsActive { get; init; }
    public bool IsDefinitionLocked { get; init; }
}
public sealed record FamilyMapEntry(Guid ArticleId, Guid FamilyId);
