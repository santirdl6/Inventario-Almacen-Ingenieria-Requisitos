using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventario.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HU08AuditInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MasterDataAudit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Before = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    After = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasterDataAudit", x => x.Id);
                    table.CheckConstraint("CK_MasterDataAudit_Action", "[Action] IN ('Create','Update','Deactivate','Reactivate','Delete')");
                    table.CheckConstraint("CK_MasterDataAudit_After", "[After] IS NULL OR ISJSON([After], VALUE) = 1");
                    table.CheckConstraint("CK_MasterDataAudit_Before", "[Before] IS NULL OR ISJSON([Before], VALUE) = 1");
                    table.CheckConstraint("CK_MasterDataAudit_Entity", "[EntityType] IN ('Family','Supplier','Article')");
                    table.ForeignKey(
                        name: "FK_MasterDataAudit_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MasterDataAudit_OperationResults_OperationId",
                        column: x => x.OperationId,
                        principalTable: "OperationResults",
                        principalColumn: "OperationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MasterDataAudit_ActorId",
                table: "MasterDataAudit",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_MasterDataAudit_EntityType_EntityId_RecordedAt",
                table: "MasterDataAudit",
                columns: new[] { "EntityType", "EntityId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MasterDataAudit_OperationId_EntityType_EntityId_Action",
                table: "MasterDataAudit",
                columns: new[] { "OperationId", "EntityType", "EntityId", "Action" },
                unique: true);
            migrationBuilder.Sql("DENY UPDATE, DELETE ON OBJECT::dbo.MasterDataAudit TO inventory_runtime;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MasterDataAudit");
        }
    }
}
