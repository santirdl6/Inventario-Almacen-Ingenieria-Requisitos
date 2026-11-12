using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inventario.Api.Infrastructure.Persistence;

public sealed class InventoryDesignTimeFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Inventory")
            ?? Environment.GetEnvironmentVariable("DESIGN_DATABASE_CONNECTION")
            ?? "Server=localhost;Database=InventoryDesign;Integrated Security=True;Encrypt=True";
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(connection)
            .Options;
        return new InventoryDbContext(options);
    }
}
