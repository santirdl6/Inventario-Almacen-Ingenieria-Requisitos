using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventario.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HU08MovementCounterparty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CounterpartyNotApplicableReason",
                table: "StockMovements",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            // Conservar los hechos anteriores sin inventar ni actualizar su evidencia.
            // NOCHECK omite la validación retrospectiva; la restricción queda habilitada
            // y se exige a todas las inserciones nuevas, también con el rol de ejecución.
            migrationBuilder.Sql("""
                ALTER TABLE [StockMovements] WITH NOCHECK
                ADD CONSTRAINT [CK_StockMovements_Counterparty] CHECK (
                    ([CounterpartyReference] IS NOT NULL AND LEN(LTRIM(RTRIM([CounterpartyReference]))) > 0)
                    OR ([CounterpartyNotApplicableReason] IS NOT NULL AND LEN(LTRIM(RTRIM([CounterpartyNotApplicableReason]))) > 0));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockMovements_Counterparty",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "CounterpartyNotApplicableReason",
                table: "StockMovements");
        }
    }
}
