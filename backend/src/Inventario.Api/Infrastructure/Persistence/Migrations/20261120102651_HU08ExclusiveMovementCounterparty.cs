using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventario.Api.Infrastructure.Persistence.Migrations
{
    public partial class HU08ExclusiveMovementCounterparty : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // La restricción sigue habilitada para las escrituras nuevas. El historial no se valida ni se modifica.
            migrationBuilder.Sql("""
                ALTER TABLE [StockMovements] DROP CONSTRAINT [CK_StockMovements_Counterparty];
                ALTER TABLE [StockMovements] WITH NOCHECK
                ADD CONSTRAINT [CK_StockMovements_Counterparty] CHECK (
                    ([CounterpartyNotApplicableReason] IS NULL AND [CounterpartyReference] IS NOT NULL
                        AND LEN(LTRIM(RTRIM([CounterpartyReference]))) > 0)
                    OR ([CounterpartyReference] IS NULL AND [CounterpartyNotApplicableReason] IS NOT NULL
                        AND LEN(LTRIM(RTRIM([CounterpartyNotApplicableReason]))) > 0));
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE [StockMovements] DROP CONSTRAINT [CK_StockMovements_Counterparty];
                ALTER TABLE [StockMovements] WITH NOCHECK
                ADD CONSTRAINT [CK_StockMovements_Counterparty] CHECK (
                    ([CounterpartyReference] IS NOT NULL AND LEN(LTRIM(RTRIM([CounterpartyReference]))) > 0)
                    OR ([CounterpartyNotApplicableReason] IS NOT NULL AND LEN(LTRIM(RTRIM([CounterpartyNotApplicableReason]))) > 0));
                """);
        }
    }
}
