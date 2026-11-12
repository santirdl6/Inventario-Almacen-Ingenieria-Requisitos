using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Inventario.Api.Infrastructure.Identity;

public sealed class PermissionClaimsTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
            return Task.FromResult(principal);

        var roles = identity.FindAll(identity.RoleClaimType).Select(claim => claim.Value).Distinct().ToArray();
        foreach (var role in roles)
        foreach (var permission in PermissionCatalog.ForRole(role))
            if (!identity.HasClaim("permission", permission))
                identity.AddClaim(new Claim("permission", permission));

        return Task.FromResult(principal);
    }
}
