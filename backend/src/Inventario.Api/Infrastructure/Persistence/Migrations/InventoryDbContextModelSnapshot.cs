using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Inventario.Api.Infrastructure.Persistence.Migrations;

[DbContext(typeof(InventoryDbContext))]
public sealed class InventoryDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.13")
            .HasAnnotation("Relational:MaxIdentifierLength", 128);
        modelBuilder.UseIdentityColumns();
    }
}
