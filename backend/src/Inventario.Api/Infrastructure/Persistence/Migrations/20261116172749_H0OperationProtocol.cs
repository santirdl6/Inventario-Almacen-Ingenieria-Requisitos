using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventario.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class H0OperationProtocol : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperationResults",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Resource = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    RequiredPermission = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResultStatus = table.Column<int>(type: "int", nullable: false),
                    ResultBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResultHeaders = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationResults", x => x.OperationId);
                    table.CheckConstraint("CK_OperationResults_Body", "[ResultBody] IS NULL OR ISJSON([ResultBody], VALUE) = 1");
                    table.CheckConstraint("CK_OperationResults_Headers", "ISJSON([ResultHeaders], OBJECT) = 1");
                    table.CheckConstraint("CK_OperationResults_Success", "[ResultStatus] >= 200 AND [ResultStatus] < 300");
                    table.ForeignKey(
                        name: "FK_OperationResults_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperationResults_ActorId_RecordedAt",
                table: "OperationResults",
                columns: new[] { "ActorId", "RecordedAt" });
            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID(N'inventory_runtime') IS NULL
                    CREATE ROLE inventory_runtime;
                DENY UPDATE, DELETE ON OBJECT::dbo.OperationResults TO inventory_runtime;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperationResults");
        }
    }
}
