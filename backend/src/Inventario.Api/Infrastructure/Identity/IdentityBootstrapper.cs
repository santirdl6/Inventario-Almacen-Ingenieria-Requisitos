using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Inventario.Api.Infrastructure.Identity;

internal static class IdentityBootstrapper
{
    public static async Task SeedAsync(IConfiguration configuration, IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var roleName in PermissionCatalog.Roles)
        {
            if (!await roles.RoleExistsAsync(roleName))
            {
                var result = await roles.CreateAsync(new IdentityRole<Guid>(roleName) { Id = Guid.NewGuid() });
                EnsureSuccess(result, $"No se pudo crear el rol {roleName}");
            }
        }

        var email = configuration["Bootstrap:AdminEmail"]?.Trim();
        var password = configuration["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            if (configuration.GetValue<bool>("Bootstrap:RequireAdmin")
                || !string.IsNullOrWhiteSpace(email) || !string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("La creación de la cuenta administrativa inicial requiere Bootstrap__AdminEmail y Bootstrap__AdminPassword.");
            return;
        }

        var admin = await users.FindByEmailAsync(email);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                LockoutEnabled = true
            };
            var created = await users.CreateAsync(admin, password);
            EnsureSuccess(created, "No se pudo crear la cuenta administrativa inicial");
        }

        if (!await users.IsInRoleAsync(admin, "Administrador"))
        {
            var assigned = await users.AddToRoleAsync(admin, "Administrador");
            EnsureSuccess(assigned, "No se pudo asignar el rol Administrador a la cuenta inicial");
        }
    }

    private static void EnsureSuccess(IdentityResult result, string message)
    {
        if (result.Succeeded)
            return;

        var details = string.Join(", ", result.Errors.Select(error => $"{error.Code}: {error.Description}"));
        throw new InvalidOperationException($"{message}: {details}");
    }
}
