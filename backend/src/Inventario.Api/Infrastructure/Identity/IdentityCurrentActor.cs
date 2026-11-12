using System.Security.Claims;
using Inventario.Api.Domain.Identity;
using Inventario.Api.Infrastructure.Persistence;

namespace Inventario.Api.Infrastructure.Identity;

internal sealed class IdentityCurrentActor(IHttpContextAccessor accessor, InventoryDbContext database)
    : ICurrentActor
{
    public async Task<Guid> GetRequiredActorIdAsync(CancellationToken cancellationToken = default)
    {
        var principal = accessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            || id == Guid.Empty)
            throw new UnauthenticatedActorException();

        var user = await database.Users.FindAsync([id], cancellationToken);
        if (user is null)
            throw new UnauthenticatedActorException();

        return user.Id;
    }
}
