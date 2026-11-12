using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Inventario.IntegrationTests;

public sealed class IdentityFixture : IAsyncLifetime
{
    public const string AdminEmail = "admin@h0.test";
    public const string Password = "IdentityTest_2026!Password";
    private readonly string suffix = Guid.NewGuid().ToString("N");
    private ServiceProvider? bootstrapServices;
    private string masterConnection = "";
    private bool initializationAttempted;

    public Dictionary<string, string?> Settings { get; } = new();
    public string KeysDirectory { get; } = Path.Combine(Path.GetTempPath(), "inventario-h0-" + Guid.NewGuid());
    public ServiceProvider BootstrapServices => bootstrapServices!;

    public async Task InitializeAsync()
    {
        // No se aceptan conexiones implícitas. La credencial debe poder crear bases de prueba.
        var configured = Environment.GetEnvironmentVariable("Testing__SqlConnection");
        var host = Environment.GetEnvironmentVariable("Testing__SqlHost");
        var password = Environment.GetEnvironmentVariable("Testing__SqlAdminPassword");
        if (string.IsNullOrWhiteSpace(configured)
            && (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(password)))
            throw new InvalidOperationException("Define Testing__SqlConnection o las variables Testing__SqlHost y Testing__SqlAdminPassword de un SQL Server de pruebas.");
        var master = !string.IsNullOrWhiteSpace(configured)
            ? new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" }
            : new SqlConnectionStringBuilder
            {
                DataSource = host, InitialCatalog = "master", UserID = "sa", Password = password,
                Encrypt = true, TrustServerCertificate = true
            };
        if (!master.UserID.Equals("sa", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La prueba requiere sa de un servidor SQL de desarrollo/pruebas.");
        masterConnection = master.ConnectionString;
        var databaseName = "InventoryH0Tests_" + suffix;
        var login = "h0_" + suffix;
        Settings["Database:Host"] = master.DataSource;
        Settings["Database:Name"] = databaseName;
        Settings["Database:User"] = login;
        Settings["Database:Password"] = Password;
        Settings["Database:AdminPassword"] = master.Password;
        Settings["Database:TrustServerCertificate"] = master.TrustServerCertificate.ToString();
        Settings["Bootstrap:AdminEmail"] = AdminEmail;
        Settings["Bootstrap:AdminPassword"] = Password;
        Settings["Bootstrap:RequireAdmin"] = "true";
        Settings["DataProtection:KeyDirectory"] = KeysDirectory;
        Settings["initialize-database"] = "false";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(Settings).Build();
        Directory.CreateDirectory(KeysDirectory);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(KeysDirectory));
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInventoryIdentity(new TestEnvironment());
        services.AddDbContext<InventoryDbContext>(options => options.UseSqlServer(
            DatabaseConfiguration.InitializationConnection(configuration), sql => sql.EnableRetryOnFailure()));
        bootstrapServices = services.BuildServiceProvider();
        initializationAttempted = true;
        await DatabaseBootstrapper.InitializeAsync(configuration, bootstrapServices);
    }

    public ApiFactory Factory(string environment = "Development") => new(Settings, environment);

    public async Task<string> CreateUserAsync(params string[] roles)
    {
        using var scope = BootstrapServices.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{Guid.NewGuid():N}@h0.test";
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = email, Email = email,
            EmailConfirmed = true, LockoutEnabled = true
        };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        Assert.True((await users.AddToRolesAsync(user, roles)).Succeeded);
        return email;
    }

    public async Task DisposeAsync()
    {
        if (bootstrapServices is not null)
            await bootstrapServices.DisposeAsync();
        SqlConnection.ClearAllPools();
        if (initializationAttempted)
        {
            var name = Settings["Database:Name"]!;
            var login = Settings["Database:User"]!;
            // Solo se eliminan nombres aleatorios generados por este fixture.
            if (name != "InventoryH0Tests_" + suffix || login != "h0_" + suffix)
                throw new InvalidOperationException("No se permite limpiar una base ajena al fixture.");
            await using var connection = new SqlConnection(masterConnection);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{name}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{name}];
                END;
                IF SUSER_ID(N'{login}') IS NOT NULL DROP LOGIN [{login}];
                """;
            await command.ExecuteNonQueryAsync();
        }
        if (Directory.Exists(KeysDirectory))
            Directory.Delete(KeysDirectory, recursive: true);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Inventario.Api";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

public sealed class ApiFactory(Dictionary<string, string?> settings, string environment)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        builder.ConfigureServices(services => services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(settings["DataProtection:KeyDirectory"]!)));
    }
}
