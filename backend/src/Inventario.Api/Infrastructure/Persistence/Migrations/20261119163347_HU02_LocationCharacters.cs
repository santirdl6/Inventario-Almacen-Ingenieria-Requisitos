using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventario.Api.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// HU-02 FR-002/FR-003 (tercera sesión de clarify, 2026-11-19): código, pasillo, estantería
    /// y nivel solo admiten A-Z, 0-9 y guion. No modifica datos: si ya hay valores que incumplen
    /// la regla, se detiene nombrando las ubicaciones afectadas para que se corrijan antes.
    /// </summary>
    public partial class HU02_LocationCharacters : Migration
    {
        private static readonly string[] Columns = ["Code", "Aisle", "Rack", "Level"];

        // Congelada en la migración (igual a LocationConfiguration.CharactersCheck en esta versión).
        private static string Check(string column) => $"[{column}] COLLATE Latin1_General_BIN2 NOT LIKE N'%[^-0-9A-Z]%'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var invalid = string.Join(" OR ", Columns.Select(column =>
                $"NOT ({Check(column)})"));
            migrationBuilder.Sql($"""
                DECLARE @offending nvarchar(max) = (
                    SELECT STRING_AGG(CONVERT(nvarchar(max), N'«' + Code + N'»'), N', ')
                    FROM (SELECT TOP (20) Code FROM dbo.Locations WHERE {invalid} ORDER BY Code) AS bad);
                IF @offending IS NOT NULL
                BEGIN
                    DECLARE @message nvarchar(2048) = CONCAT(
                        N'HU02_LocationCharacters: hay ubicaciones con caracteres no admitidos en el código, ',
                        N'el pasillo, la estantería o el nivel (solo A-Z sin acentos ni Ñ, dígitos y guion): ',
                        @offending,
                        N'. Corrígelas desde la aplicación antes de migrar; la migración no modifica datos.');
                    THROW 50003, @message, 1;
                END
                """);
            foreach (var column in Columns)
                migrationBuilder.AddCheckConstraint(
                    name: $"CK_Locations_{column}_Chars",
                    table: "Locations",
                    sql: Check(column));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var column in Columns)
                migrationBuilder.DropCheckConstraint(name: $"CK_Locations_{column}_Chars", table: "Locations");
        }
    }
}
