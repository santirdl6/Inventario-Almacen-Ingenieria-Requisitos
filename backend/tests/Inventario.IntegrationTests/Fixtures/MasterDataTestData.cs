using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Inventario.IntegrationTests.Fixtures;

public static class MasterDataTestData
{
    public const string E2EDatabase = "InventoryHu08E2E";
    private static readonly string[] E2ERoles = ["GestorMaestros", "Operario", "Administrador"];

    public static ServiceProvider BootstrapServices(IConfiguration configuration, string connection, string keysDirectory)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));
        services.AddSingleton(configuration);
        services.AddInventoryIdentity(new IdentityFixture.TestEnvironment());
        services.AddDbContext<InventoryDbContext>(options => options.UseSqlServer(
            connection, sql => sql.EnableRetryOnFailure()));
        return services.BuildServiceProvider();
    }

    public static InventoryDbContext ApplicationDatabase(IConfiguration configuration) =>
        new(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlServer(
            DatabaseConfiguration.ApplicationConnection(configuration), sql => sql.EnableRetryOnFailure()).Options);

    public static async Task<TestAccount> CreateAccountAsync(IServiceProvider services, string email,
        string password, params string[] roles)
    {
        var requestedRoles = roles.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (requestedRoles.Length == 0 || requestedRoles.Any(role => !PermissionCatalog.Roles.Contains(role)))
            throw new ArgumentException("La cuenta de prueba requiere roles del catálogo existente.", nameof(roles));

        // Identidad estable incluso si se pierde la confirmación del commit durante un reintento.
        var id = Guid.NewGuid();
        using var strategyScope = services.CreateScope();
        var strategy = strategyScope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            .Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var scope = services.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            await using var transaction = await database.Database.BeginTransactionAsync();
            foreach (var role in requestedRoles)
                if (!await roleManager.RoleExistsAsync(role))
                    throw new InvalidOperationException("Inicializa los roles de Identity antes de preparar cuentas.");

            var existing = await users.FindByEmailAsync(email);
            if (existing is not null)
            {
                var actualRoles = (await users.GetRolesAsync(existing)).Order(StringComparer.Ordinal);
                if (existing.Id == id && await users.CheckPasswordAsync(existing, password)
                    && actualRoles.SequenceEqual(requestedRoles))
                    return new TestAccount(id, email, password, requestedRoles);
                throw new InvalidOperationException("No se sobrescribe una cuenta de prueba existente.");
            }

            var user = new ApplicationUser
            {
                Id = id, UserName = email, Email = email,
                EmailConfirmed = true, LockoutEnabled = true
            };
            RequireSuccess(await users.CreateAsync(user, password));
            RequireSuccess(await users.AddToRolesAsync(user, requestedRoles));
            await transaction.CommitAsync();
            return new TestAccount(id, email, password, requestedRoles);
        });
    }

    public static void ValidateE2ERequest(string database, string host, IReadOnlyList<E2EAccount> accounts)
    {
        if (database != E2EDatabase || host != "sqlserver")
            throw new ArgumentException("El provisionamiento solo admite InventoryHu08E2E en el servicio sqlserver dedicado.");
        if (accounts.Count != 3 || !accounts.Select(account => account.Role).Order(StringComparer.Ordinal)
            .SequenceEqual(E2ERoles.Order(StringComparer.Ordinal)))
            throw new ArgumentException("Se requieren exactamente las cuentas gestora, lectora y administradora de E2E.");

        var runs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var account in accounts)
        {
            var parts = account.Email.Split('@');
            var local = parts[0].Split('.');
            if (parts.Length != 2 || parts[1] != "hu08.e2e.test" || local.Length != 2
                || !Guid.TryParseExact(local[0], "N", out _) || local[1] != account.Role.ToLowerInvariant()
                || string.IsNullOrWhiteSpace(account.Password))
                throw new ArgumentException("Las cuentas E2E deben pertenecer al identificador de ejecución y dominio de pruebas.");
            runs.Add(local[0]);
        }
        if (runs.Count != 1)
            throw new ArgumentException("Las tres cuentas deben pertenecer a la misma ejecución E2E.");
    }

    public static async Task PrepareE2EAsync(string database, string host, string adminPassword,
        IReadOnlyList<E2EAccount> accounts, bool retire)
    {
        ValidateE2ERequest(database, host, accounts);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminPassword);
        var connection = new SqlConnectionStringBuilder
        {
            DataSource = host, InitialCatalog = database, UserID = "sa", Password = adminPassword,
            Encrypt = true, TrustServerCertificate = true
        }.ConnectionString;
        var keysDirectory = Path.Combine(Path.GetTempPath(), "inventario-e2e-provision-" + Guid.NewGuid());
        Directory.CreateDirectory(keysDirectory);
        try
        {
            var configuration = new ConfigurationBuilder().Build();
            await using var services = BootstrapServices(configuration, connection, keysDirectory);
            using (var scope = services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
                if ((await context.Database.GetPendingMigrationsAsync()).Any())
                    throw new InvalidOperationException("Inicializa el entorno E2E con las migraciones del código actual.");
            }

            foreach (var account in accounts)
            {
                if (!retire)
                {
                    await CreateAccountAsync(services, account.Email, account.Password, account.Role);
                    continue;
                }

                // No borrar actores: las futuras pruebas de maestros pueden dejar auditoría referenciada.
                using var scope = services.CreateScope();
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await users.FindByEmailAsync(account.Email);
                if (user is null)
                    continue;
                var roles = await users.GetRolesAsync(user);
                if (!await users.CheckPasswordAsync(user, account.Password)
                    || roles.Count != 1 || roles[0] != account.Role)
                    throw new InvalidOperationException("No se modifica una cuenta ajena al fixture E2E.");
                RequireSuccess(await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue));
                RequireSuccess(await users.UpdateSecurityStampAsync(user));
            }
        }
        finally
        {
            if (Directory.Exists(keysDirectory))
                Directory.Delete(keysDirectory, recursive: true);
        }
    }

    private static void RequireSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException("No se pudo preparar Identity: "
                + string.Join(", ", result.Errors.Select(error => error.Code)));
    }
}

public sealed record TestAccount(Guid Id, string Email, string Password, string[] Roles)
{
    public override string ToString() => $"Cuenta de prueba {Id}";
}

public sealed record E2EAccount(string Email, string Password, string Role)
{
    public override string ToString() => "Cuenta de la ejecución E2E";
}
