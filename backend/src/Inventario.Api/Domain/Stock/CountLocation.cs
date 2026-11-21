namespace Inventario.Api.Domain.Stock;

/// <summary>
/// Ubicación del alcance de un conteo. <see cref="IsFrozen"/> es la congelación (research D-04): 1 de
/// Counting a cerrar y 0 en Draft y tras cerrar. Un índice único filtrado impide dos conteos abiertos
/// sobre la misma ubicación y TR_CountLocations_Scope, volver a congelarla con un conteo cerrado (D-07).
/// </summary>
public sealed class CountLocation
{
    private CountLocation() { }

    internal CountLocation(Guid countId, Guid locationId)
    {
        CountId = countId;
        LocationId = locationId;
    }

    public Guid CountId { get; private set; }
    public Guid LocationId { get; private set; }
    public bool IsFrozen { get; private set; }

    internal void Freeze() => IsFrozen = true;

    internal void Release() => IsFrozen = false;
}
