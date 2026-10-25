using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inventario.Api.Infrastructure.Persistence.Migrations;

[DbContext(typeof(InventoryDbContext))]
[Migration("202610250001_InitialInfrastructure")]
public sealed class InitialInfrastructure : Migration
{
    // Crea la base y el historial de migraciones; las tablas de negocio pertenecen a sus HU.
    protected override void Up(MigrationBuilder migrationBuilder) { }
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
