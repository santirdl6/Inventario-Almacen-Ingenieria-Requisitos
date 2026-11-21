using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventario.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HU08MasterDataExpand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FamilyId",
                table: "Articles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefinitionLocked",
                table: "Articles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "StockPolicies",
                columns: table => new
                {
                    ArticleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MinimumAvailable = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockPolicies", x => x.ArticleId);
                    table.CheckConstraint("CK_StockPolicies_NonNegative", "[MinimumAvailable] >= 0");
                    table.ForeignKey(
                        name: "FK_StockPolicies_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Articles_FamilyId",
                table: "Articles",
                column: "FamilyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Articles_Families_FamilyId",
                table: "Articles",
                column: "FamilyId",
                principalTable: "Families",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Articles_Families_FamilyId",
                table: "Articles");

            migrationBuilder.DropTable(
                name: "StockPolicies");

            migrationBuilder.DropIndex(
                name: "IX_Articles_FamilyId",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "FamilyId",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "IsDefinitionLocked",
                table: "Articles");
        }
    }
}
