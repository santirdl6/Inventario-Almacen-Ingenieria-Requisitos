using Inventario.IntegrationTests.Fixtures;
using Xunit;

namespace Inventario.IntegrationTests.Stock;

// HU-05 T018: el aprovisionamiento E2E de A admite, además de las tres cuentas comunes, una cuarta
// ResponsableAlmacen (único rol con Reservations.Manage) para E2E-01. Sin base de datos.
public sealed class E2EProvisioningTests
{
    private const string Database = MasterDataTestData.E2EDatabase;
    private static readonly string Run = Guid.NewGuid().ToString("N");

    private static E2EAccount Account(string role, string run = "") =>
        new($"{(run.Length > 0 ? run : Run)}.{role.ToLowerInvariant()}@hu08.e2e.test", "Clave_E2E_1!", role);

    private static readonly E2EAccount[] Common =
        [Account("GestorMaestros"), Account("Operario"), Account("Administrador")];

    [Fact]
    public void Accepts_the_three_common_accounts_and_an_optional_warehouse_manager()
    {
        MasterDataTestData.ValidateE2ERequest(Database, "sqlserver", Common);
        MasterDataTestData.ValidateE2ERequest(Database, "sqlserver", [.. Common, Account("ResponsableAlmacen")]);
    }

    [Fact]
    public void Rejects_other_fourth_roles_duplicates_and_mixed_runs()
    {
        Assert.Throws<ArgumentException>(() => MasterDataTestData.ValidateE2ERequest(Database, "sqlserver",
            [.. Common, Account("Comprador")]));
        Assert.Throws<ArgumentException>(() => MasterDataTestData.ValidateE2ERequest(Database, "sqlserver",
            [.. Common, Account("Operario")]));
        Assert.Throws<ArgumentException>(() => MasterDataTestData.ValidateE2ERequest(Database, "sqlserver",
            [.. Common, Account("ResponsableAlmacen"), Account("ResponsableAlmacen")]));
        Assert.Throws<ArgumentException>(() => MasterDataTestData.ValidateE2ERequest(Database, "sqlserver",
            [.. Common, Account("ResponsableAlmacen", Guid.NewGuid().ToString("N"))]));
        Assert.Throws<ArgumentException>(() => MasterDataTestData.ValidateE2ERequest(Database, "sqlserver",
            [Account("GestorMaestros"), Account("Operario"), Account("ResponsableAlmacen")]));
    }
}
