using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventario.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HU08RequireArticleFamily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.Articles AS a WHERE a.FamilyId IS NULL
                    OR NOT EXISTS (SELECT 1 FROM dbo.StockPolicies AS p WHERE p.ArticleId = a.Id))
                    THROW 51002, N'Completa la preparación autenticada de familia y política antes del cierre HU08.', 1;
                """);
            migrationBuilder.AlterColumn<Guid>(name: "FamilyId", table: "Articles", type: "uniqueidentifier",
                nullable: false, oldClrType: typeof(Guid), oldType: "uniqueidentifier", oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(name: "FamilyId", table: "Articles", type: "uniqueidentifier",
                nullable: true, oldClrType: typeof(Guid), oldType: "uniqueidentifier");
        }
    }
}
