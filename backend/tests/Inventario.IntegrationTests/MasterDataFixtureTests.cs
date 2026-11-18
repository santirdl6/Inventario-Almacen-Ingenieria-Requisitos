using Inventario.Api.Infrastructure.Identity;
using Inventario.IntegrationTests.Fixtures;
using Inventario.Api.Domain.MasterData;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests;

public sealed class MasterDataFixtureTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task Controlled_accounts_have_real_passwords_and_the_requested_roles_only()
    {
        using var scope = fixture.BootstrapServices.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var authorization = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var transform = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Authentication.IClaimsTransformation>();

        foreach (var role in new[] { "GestorMaestros", "Operario", "Administrador" })
        {
            var email = await fixture.CreateUserAsync(role);
            var user = (await users.FindByEmailAsync(email))!;
            Assert.True(await users.CheckPasswordAsync(user, IdentityFixture.Password));
            Assert.Equal(new[] { role }, await users.GetRolesAsync(user));
            var principal = await transform.TransformAsync(await signIn.CreateUserPrincipalAsync(user));
            Assert.Equal(role == "GestorMaestros",
                (await authorization.AuthorizeAsync(principal, null, "MasterData.Manage")).Succeeded);
            Assert.Equal(role != "Administrador",
                (await authorization.AuthorizeAsync(principal, null, "Inventory.Read")).Succeeded);
        }
    }

    [Fact]
    public async Task Independent_restricted_connections_observe_only_committed_test_data()
    {
        await using var first = fixture.ApplicationDatabase();
        await using var second = fixture.ApplicationDatabase();
        await first.Database.OpenConnectionAsync();
        await second.Database.OpenConnectionAsync();
        await using var one = first.Database.GetDbConnection().CreateCommand();
        one.CommandText = "SELECT @@SPID";
        await using var two = second.Database.GetDbConnection().CreateCommand();
        two.CommandText = "SELECT @@SPID";
        Assert.NotEqual(Convert.ToInt32(await one.ExecuteScalarAsync()), Convert.ToInt32(await two.ExecuteScalarAsync()));
        two.CommandText = "SELECT CONVERT(int, IS_SRVROLEMEMBER('sysadmin')) + COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE'), 0)";
        Assert.Equal(0, Convert.ToInt32(await two.ExecuteScalarAsync()));

        var article = await fixture.NewArticleAsync("FIX-" + Guid.NewGuid().ToString("N"), "Referencia del fixture", "UD", false, false);
        first.Articles.Add(article);
        Assert.False(await second.Articles.AnyAsync(value => value.Id == article.Id));
        await first.SaveChangesAsync();
        Assert.True(await second.Articles.AnyAsync(value => value.Id == article.Id));
    }

    [Theory]
    [InlineData("InventoryDev", "sqlserver")]
    [InlineData("InventoryHu08E2E", "localhost")]
    public void E2e_provisioning_rejects_other_database_targets_before_opening_a_connection(string database, string host)
    {
        Assert.Throws<ArgumentException>(() => MasterDataTestData.ValidateE2ERequest(database, host, []));
    }
}
