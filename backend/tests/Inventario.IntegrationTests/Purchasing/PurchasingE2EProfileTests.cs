using Inventario.IntegrationTests.Fixtures;
using Xunit;

namespace Inventario.IntegrationTests.Purchasing;

public sealed class PurchasingE2EProfileTests
{
    [Fact]
    public void Buyer_requires_an_explicit_matching_database_profile_and_run()
    {
        var run = Guid.NewGuid().ToString("N");
        E2EAccount[] Accounts(string domain) => new[] { "GestorMaestros", "Operario", "Administrador", "Comprador" }
            .Select(role => new E2EAccount($"{run}.{role.ToLowerInvariant()}@{domain}", IdentityFixture.Password, role)).ToArray();
        var purchasing = Accounts("hu07.e2e.test");
        MasterDataTestData.ValidateE2ERequest("InventoryHu07E2E", "sqlserver", purchasing, "hu07");
        Assert.Throws<ArgumentException>(() => MasterDataTestData.ValidateE2ERequest("InventoryHu07E2E", "sqlserver", purchasing));
        Assert.Throws<ArgumentException>(() => MasterDataTestData.ValidateE2ERequest("InventoryHu08E2E", "sqlserver", purchasing, "hu07"));
        Assert.Throws<ArgumentException>(() => MasterDataTestData.ValidateE2ERequest("InventoryDev", "sqlserver", purchasing, "hu07"));
        Assert.Throws<ArgumentException>(() => MasterDataTestData.ValidateE2ERequest("InventoryHu07E2E", "sqlserver", Accounts("hu08.e2e.test"), "hu07"));
        MasterDataTestData.ValidateE2ERequest("InventoryHu08E2E", "sqlserver", Accounts("hu08.e2e.test")[..3]);
    }
}
