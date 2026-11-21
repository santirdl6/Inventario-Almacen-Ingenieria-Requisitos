using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventario.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HU07ArticleLookupIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Articles_IsActive_Sku_Id",
                table: "Articles",
                columns: new[] { "IsActive", "Sku", "Id" })
                .Annotation("SqlServer:Include", new[] { "FamilyId", "Name", "BaseUnit", "AllowsFractional", "TracksLot" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Articles_IsActive_Sku_Id",
                table: "Articles");
        }
    }
}
