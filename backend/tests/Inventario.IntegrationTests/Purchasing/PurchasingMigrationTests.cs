using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests.Purchasing;

public sealed class PurchasingMigrationTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task Additive_migration_upgrades_existing_data_and_runtime_enforces_constraints_and_append_only()
    {
        using var adminScope = fixture.BootstrapServices.CreateScope();
        var admin = adminScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var migrator = admin.GetService<IMigrator>();
        var supplier = new Supplier(Guid.NewGuid(), "PROV-MIG", "Proveedor anterior", null, null);
        var article = await fixture.NewArticleAsync("ART-MIG", "Artículo anterior", "UD", false, false);
        await using (var before = fixture.ApplicationDatabase())
        {
            before.Add(supplier); before.Add(article); await before.SaveChangesAsync();
        }
        await migrator.MigrateAsync("20261119171332_HU08MovementCounterparty");
        await using (var old = fixture.ApplicationDatabase()) Assert.True(await old.Articles.AnyAsync(value => value.Id == article.Id));
        await migrator.MigrateAsync();
        await using var db = fixture.ApplicationDatabase();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal("Artículo anterior", (await db.Articles.SingleAsync(value => value.Id == article.Id)).Name);
        Assert.Equal("Proveedor anterior", (await db.Set<Supplier>().SingleAsync(value => value.Id == supplier.Id)).Name);
        var actor = (await db.Set<ApplicationUser>().SingleAsync(user => user.Email == IdentityFixture.AdminEmail)).Id;
        var id = Guid.NewGuid();
        var line = PurchaseOrderLine.Create(Guid.NewGuid(), id, 1, PurchaseArticleSnapshot.From(article), 10m, 0m, new(2026, 12, 1));
        var order = PurchaseOrder.Create(id, PurchaseSupplierSnapshot.From(supplier), "EUR", new(2026, 12, 1), actor, [line]);
        db.Add(order); await db.SaveChangesAsync();
        Assert.Matches("^PO-[0-9]{8,}$", order.Number);
        Assert.NotEmpty(order.Version);
        Assert.Equal(0m, (await db.Set<PurchaseOrderLine>().SingleAsync(value => value.Id == line.Id)).ReceivedQuantity);
        foreach (var command in new[]
        {
            "UPDATE dbo.PurchaseOrderDeliveries SET DocumentName=DocumentName WHERE 1=0",
            "DELETE FROM dbo.PurchaseOrderDeliveries WHERE 1=0",
            "UPDATE dbo.PurchaseOrderAudit SET Action=Action WHERE 1=0",
            "DELETE FROM dbo.PurchaseOrderAudit WHERE 1=0",
            "UPDATE dbo.PurchaseDeliveryEvents SET Outcome=Outcome WHERE 1=0",
            "DELETE FROM dbo.PurchaseDeliveryEvents WHERE 1=0"
        })
        {
            var denied = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(command));
            Assert.Equal(229, denied.Number);
        }
        var quantity = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.PurchaseOrderLines SET OrderedQuantity=0 WHERE Id={line.Id}"));
        Assert.Equal(547, quantity.Number);
        var state = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.PurchaseOrders SET State='Sent' WHERE Id={id}"));
        Assert.Equal(547, state.Number);
        var json = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.PurchaseOrders SET SupplierSnapshot='invalid' WHERE Id={id}"));
        Assert.Equal(547, json.Number);
    }
}
