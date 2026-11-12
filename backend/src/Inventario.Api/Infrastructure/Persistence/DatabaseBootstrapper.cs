using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Inventario.Api.Infrastructure.Identity;

namespace Inventario.Api.Infrastructure.Persistence;

internal static class DatabaseBootstrapper
{
    public static async Task InitializeAsync(IConfiguration configuration, IServiceProvider services)
    {
        var name = DatabaseConfiguration.Identifier(configuration["Database:Name"] ?? "InventoryDev");
        var user = DatabaseConfiguration.Identifier(configuration["Database:User"] ?? "inventory_app");
        var password = configuration["Database:Password"]
            ?? throw new InvalidOperationException("Falta Database__Password.");
        var administrator = DatabaseConfiguration.AdministratorConnection(configuration);
        var target = new SqlConnectionStringBuilder(administrator) { InitialCatalog = name };

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(target.ConnectionString, sql => sql.EnableRetryOnFailure())
            .Options;
        await using (var database = new InventoryDbContext(options))
            await database.Database.MigrateAsync();

        await using var connection = new SqlConnection(administrator);
        await connection.OpenAsync();
        await using (var settings = connection.CreateCommand())
        {
            settings.CommandText = $"""
                IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'{name}' AND is_read_committed_snapshot_on = 0)
                    ALTER DATABASE [{name}] SET READ_COMMITTED_SNAPSHOT ON;
                IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'{name}' AND snapshot_isolation_state = 0)
                    ALTER DATABASE [{name}] SET ALLOW_SNAPSHOT_ISOLATION ON;
                """;
            await settings.ExecuteNonQueryAsync();
        }

        await using (var login = connection.CreateCommand())
        {
            login.CommandText = """
                DECLARE @sql nvarchar(max);
                IF NOT EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = @name)
                BEGIN
                    SET @sql = N'CREATE LOGIN ' + QUOTENAME(@name)
                        + N' WITH PASSWORD = ' + QUOTENAME(@password, '''') + N', CHECK_POLICY = ON';
                    EXEC sys.sp_executesql @sql;
                END;
                """;
            login.Parameters.AddWithValue("@name", user);
            login.Parameters.AddWithValue("@password", password);
            await login.ExecuteNonQueryAsync();
        }

        await connection.ChangeDatabaseAsync(name);
        await using (var permissions = connection.CreateCommand())
        {
            permissions.CommandText = $"""
                IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'{user}')
                    CREATE USER [{user}] FOR LOGIN [{user}];
                GRANT CONNECT TO [{user}];
                IF IS_ROLEMEMBER(N'db_datareader', N'{user}') <> 1
                    ALTER ROLE db_datareader ADD MEMBER [{user}];
                IF IS_ROLEMEMBER(N'db_datawriter', N'{user}') <> 1
                    ALTER ROLE db_datawriter ADD MEMBER [{user}];
                """;
            await permissions.ExecuteNonQueryAsync();
        }

        // El inicializador no termina con éxito si las credenciales de ejecución no funcionan.
        await using var application = new SqlConnection(DatabaseConfiguration.ApplicationConnection(configuration));
        await application.OpenAsync();

        // El scope de Identity usa la conexión administrativa solo en el modo initialize-database.
        // Sembrar después de crear el usuario SQL permite también arrancar sobre una base vacía.
        await IdentityBootstrapper.SeedAsync(configuration, services);
    }
}
