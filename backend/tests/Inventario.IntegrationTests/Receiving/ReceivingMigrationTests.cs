using Inventario.Api.Domain.Receiving;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests.Receiving;

public sealed class ReceivingMigrationTests(ReceivingFixture fixture) : IClassFixture<ReceivingFixture>
{
    [Fact]
    public async Task Upgrade_preserves_purchases_and_enforces_receipt_constraints()
    {
        var (order, line, _, location) = await fixture.SentOrderAsync();
        using var scope = fixture.Database.BootstrapServices.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var migrator = admin.GetService<IMigrator>();
        await migrator.MigrateAsync("20261121025020_HU07ArticleLookupIndex");
        await migrator.MigrateAsync();
        await using var db = fixture.Database.ApplicationDatabase();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var actor = await db.Users.Select(value => value.Id).FirstAsync();
        var receipt = new Receipt(Guid.NewGuid(), order, actor, DateTimeOffset.UtcNow);
        var item = new ReceiptLine(Guid.NewGuid(), receipt.Id, line);
        var allocation = new ReceiptAllocation(Guid.NewGuid(), item.Id);
        allocation.Revise(location, null, 2);
        item.Revise(2, 2, null, [allocation]);
        receipt.Replace(DateTimeOffset.UtcNow, "ALB-1", null, "Entrada", [item], []);
        db.Add(receipt);
        await db.SaveChangesAsync();
        Assert.NotEmpty(receipt.Version);
        foreach (var statement in new FormattableString[] {
            $"UPDATE dbo.Receipts SET State='Confirmed' WHERE Id={receipt.Id}",
            $"UPDATE dbo.ReceiptLines SET AcceptedQuantity=-1 WHERE Id={item.Id}",
            $"UPDATE dbo.ReceiptLines SET ReportedQuantity=3 WHERE Id={item.Id}",
            $"UPDATE dbo.ReceiptAllocations SET Quantity=0 WHERE Id={allocation.Id}",
            $"UPDATE dbo.ReceiptAllocations SET LocationId={Guid.NewGuid()} WHERE Id={allocation.Id}"
        })
            Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(statement))).Number);
        foreach (var statement in new[] {
            "UPDATE dbo.ContingencyReferences SET ResponsibleReference=ResponsibleReference WHERE 1=0",
            "DELETE FROM dbo.ContingencyReferences WHERE 1=0",
            "DELETE FROM dbo.ReceiptContingencyScopes WHERE 1=0"
        })
            Assert.Equal(229, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(statement))).Number);
    }
}
