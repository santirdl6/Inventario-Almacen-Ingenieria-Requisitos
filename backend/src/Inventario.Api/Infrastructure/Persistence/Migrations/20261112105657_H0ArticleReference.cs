using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventario.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class H0ArticleReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Articles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BaseUnit = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AllowsFractional = table.Column<bool>(type: "bit", nullable: false),
                    TracksLot = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Articles", x => x.Id);
                    table.CheckConstraint("CK_Articles_BaseUnit_NotBlank", "LEN(LTRIM(RTRIM([BaseUnit]))) > 0");
                    table.CheckConstraint("CK_Articles_Name_NotBlank", "LEN(LTRIM(RTRIM([Name]))) > 0");
                    table.CheckConstraint("CK_Articles_Sku_NotBlank", "LEN(LTRIM(RTRIM([Sku]))) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Articles_Sku",
                table: "Articles",
                column: "Sku",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Articles");
        }
    }
}
