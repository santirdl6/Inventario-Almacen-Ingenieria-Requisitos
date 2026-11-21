namespace Inventario.Api.Domain.Identity;

public interface ICurrentActor
{
    // Devuelve el usuario autenticado existente; nunca acepta un ActorId de la petición.
    Task<Guid> GetRequiredActorIdAsync(CancellationToken cancellationToken = default);
}
