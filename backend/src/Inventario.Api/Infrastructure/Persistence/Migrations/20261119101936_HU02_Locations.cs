using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventario.Api.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// HU-02 (aditiva sobre H0_StockCore): lugar físico único (FR-003) con collation sin
    /// distinguir mayúsculas, y la entidad Location en la auditoría común de A (FR-015).
    /// No borra ni renombra datos: si ya hay lugares repetidos, falla con un mensaje explícito.
    /// </summary>
    public partial class HU02_Locations : Migration
    {
        private static readonly string[] Parts = ["Aisle", "Rack", "Level"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM dbo.Locations
                    GROUP BY UPPER(LTRIM(RTRIM(Aisle))), UPPER(LTRIM(RTRIM(Rack))), UPPER(LTRIM(RTRIM(Level)))
                    HAVING COUNT(*) > 1)
                    THROW 50002, N'HU02_Locations: hay ubicaciones con el mismo pasillo, estantería y nivel. Corrígelas antes de migrar; la migración no borra ni renombra datos.', 1;
                """);

            // ALTER COLUMN no cambia la collation mientras un CHECK dependa de la columna.
            foreach (var part in Parts)
                migrationBuilder.DropCheckConstraint(name: $"CK_Locations_{part}_NotBlank", table: "Locations");
            foreach (var part in Parts)
                migrationBuilder.AlterColumn<string>(
                    name: part,
                    table: "Locations",
                    type: "nvarchar(16)",
                    maxLength: 16,
                    nullable: false,
                    collation: "Latin1_General_100_CI_AS",
                    oldClrType: typeof(string),
                    oldType: "nvarchar(16)",
                    oldMaxLength: 16);
            foreach (var part in Parts)
                migrationBuilder.AddCheckConstraint(name: $"CK_Locations_{part}_NotBlank", table: "Locations",
                    sql: $"LEN(LTRIM(RTRIM([{part}]))) > 0");

            migrationBuilder.CreateIndex(
                name: "UX_Locations_Aisle_Rack_Level",
                table: "Locations",
                columns: new[] { "Aisle", "Rack", "Level" },
                unique: true);

            migrationBuilder.DropCheckConstraint(name: "CK_MasterDataAudit_Entity", table: "MasterDataAudit");
            migrationBuilder.AddCheckConstraint(
                name: "CK_MasterDataAudit_Entity",
                table: "MasterDataAudit",
                sql: "[EntityType] IN ('Family','Supplier','Article','Location')");
        }

        /// <inheritdoc />
        /// <remarks>Si ya hay auditoría de ubicaciones, el CHECK anterior no se puede restaurar
        /// y la reversión falla: la evidencia append-only no se borra para revertir.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "CK_MasterDataAudit_Entity", table: "MasterDataAudit");
            migrationBuilder.AddCheckConstraint(
                name: "CK_MasterDataAudit_Entity",
                table: "MasterDataAudit",
                sql: "[EntityType] IN ('Family','Supplier','Article')");

            migrationBuilder.DropIndex(name: "UX_Locations_Aisle_Rack_Level", table: "Locations");

            foreach (var part in Parts)
                migrationBuilder.DropCheckConstraint(name: $"CK_Locations_{part}_NotBlank", table: "Locations");
            foreach (var part in Parts)
                migrationBuilder.AlterColumn<string>(
                    name: part,
                    table: "Locations",
                    type: "nvarchar(16)",
                    maxLength: 16,
                    nullable: false,
                    oldClrType: typeof(string),
                    oldType: "nvarchar(16)",
                    oldMaxLength: 16,
                    oldCollation: "Latin1_General_100_CI_AS");
            foreach (var part in Parts)
                migrationBuilder.AddCheckConstraint(name: $"CK_Locations_{part}_NotBlank", table: "Locations",
                    sql: $"LEN(LTRIM(RTRIM([{part}]))) > 0");
        }
    }
}
