namespace Inventario.Api.Domain.MasterData;

public sealed class Family
{
    private Family() { }
    internal Family(Guid id, string code, string name) { Id = id; Code = code; Name = name; }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public bool IsActive { get; private set; } = true;
    public byte[] Version { get; private set; } = [];
    internal void Change(string code, string name, bool active) { Code = code; Name = name; IsActive = active; }
}
