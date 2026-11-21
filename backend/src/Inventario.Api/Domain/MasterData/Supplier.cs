namespace Inventario.Api.Domain.MasterData;

public sealed class Supplier
{
    private Supplier() { }
    internal Supplier(Guid id, string code, string name, string? email, string? taxIdentifier)
    { Id = id; Change(code, name, email, taxIdentifier, true); }
    public Guid Id { get; private set; }
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string? Email { get; private set; }
    public string? TaxIdentifier { get; private set; }
    public bool IsActive { get; private set; } = true;
    public byte[] Version { get; private set; } = [];
    internal void Change(string code, string name, string? email, string? taxIdentifier, bool active)
    { Code = code; Name = name; Email = email; TaxIdentifier = taxIdentifier; IsActive = active; }
}
