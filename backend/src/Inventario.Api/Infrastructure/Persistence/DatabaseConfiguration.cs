using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Inventario.Api.Infrastructure.Persistence;

internal static partial class DatabaseConfiguration
{
    public static string ApplicationConnection(IConfiguration configuration) =>
        Build(configuration, administrator: false);

    public static string AdministratorConnection(IConfiguration configuration) =>
        Build(configuration, administrator: true);

    public static string InitializationConnection(IConfiguration configuration) =>
        new SqlConnectionStringBuilder(AdministratorConnection(configuration))
        {
            InitialCatalog = Identifier(configuration["Database:Name"] ?? "InventoryDev")
        }.ConnectionString;

    public static string Identifier(string value)
    {
        if (!IdentifierPattern().IsMatch(value))
            throw new InvalidOperationException("El nombre de base o usuario debe tener entre 1 y 64 caracteres alfanuméricos o guiones bajos y empezar por una letra.");
        return value;
    }

    private static string Build(IConfiguration configuration, bool administrator)
    {
        var passwordKey = administrator ? "Database:AdminPassword" : "Database:Password";
        var password = configuration[passwordKey];
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException($"Falta la variable {passwordKey.Replace(":", "__")}.");

        return new SqlConnectionStringBuilder
        {
            DataSource = configuration["Database:Host"] ?? "localhost",
            InitialCatalog = administrator ? "master" : Identifier(configuration["Database:Name"] ?? "InventoryDev"),
            UserID = administrator ? "sa" : Identifier(configuration["Database:User"] ?? "inventory_app"),
            Password = password,
            Encrypt = true,
            TrustServerCertificate = configuration.GetValue("Database:TrustServerCertificate", false),
            ConnectTimeout = 15
        }.ConnectionString;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$")]
    private static partial Regex IdentifierPattern();
}
