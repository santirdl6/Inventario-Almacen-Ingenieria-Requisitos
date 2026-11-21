using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventario.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HU06StockAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PhysicalSnapshot = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ReservedSnapshot = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    AvailableSnapshot = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    MinimumSnapshot = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    SuggestedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockAlerts", x => x.Id);
                    table.CheckConstraint("CK_StockAlerts_ResolvedAt", "([State] = 'Active' AND [ResolvedAt] IS NULL) OR ([State] = 'Resolved' AND [ResolvedAt] IS NOT NULL)");
                    table.CheckConstraint("CK_StockAlerts_State", "[State] IN ('Active', 'Resolved')");
                    table.CheckConstraint("CK_StockAlerts_SuggestedQuantity", "[SuggestedQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_StockAlerts_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockAlerts_ArticleId_Active",
                table: "StockAlerts",
                column: "ArticleId",
                unique: true,
                filter: "[State] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_StockAlerts_ArticleId_OpenedAt",
                table: "StockAlerts",
                columns: new[] { "ArticleId", "OpenedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_StockAlerts_State_OpenedAt",
                table: "StockAlerts",
                columns: new[] { "State", "OpenedAt" },
                descending: new[] { false, true })
                .Annotation("SqlServer:Include", new[] { "ArticleId", "AvailableSnapshot", "MinimumSnapshot", "SuggestedQuantity" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockAlerts");
        }
    }
}
