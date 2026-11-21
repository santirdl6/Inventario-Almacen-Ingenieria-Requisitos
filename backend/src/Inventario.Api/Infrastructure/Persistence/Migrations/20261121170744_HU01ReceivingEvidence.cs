using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventario.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HU01ReceivingEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContingencyReferences",
                columns: table => new
                {
                    VoucherId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResponsibleReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NormalizedData = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReconciledOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReconciledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContingencyReferences", x => x.VoucherId);
                    table.CheckConstraint("CK_ContingencyReferences_Data", "ISJSON([NormalizedData]) = 1");
                    table.CheckConstraint("CK_ContingencyReferences_Type", "[Type] = 'Receive'");
                    table.ForeignKey(
                        name: "FK_ContingencyReferences_AspNetUsers_ImportedBy",
                        column: x => x.ImportedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContingencyReferences_OperationResults_ReconciledOperationId",
                        column: x => x.ReconciledOperationId,
                        principalTable: "OperationResults",
                        principalColumn: "OperationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContingencyReferences_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptContingencyScopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PurchaseOrders = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Locations = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpectedVouchers = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Differences = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    JournalEvidence = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StockEvidence = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PurchaseEvidence = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AuthorizedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AuthorizedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AuthorizationOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevisionStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptContingencyScopes", x => x.Id);
                    table.CheckConstraint("CK_ReceiptContingencyScopes_Json", "ISJSON([PurchaseOrders]) = 1 AND ISJSON([Locations]) = 1 AND ISJSON([ExpectedVouchers]) = 1 AND ISJSON([Differences]) = 1");
                    table.CheckConstraint("CK_ReceiptContingencyScopes_State", "([State] = 'Suspended' AND [AuthorizedBy] IS NULL AND [AuthorizedAt] IS NULL AND [AuthorizationOperationId] IS NULL) OR ([State] = 'ReopeningAuthorized' AND [AuthorizedBy] IS NOT NULL AND [AuthorizedAt] IS NOT NULL AND [AuthorizationOperationId] IS NOT NULL AND [JournalEvidence] IS NOT NULL AND [StockEvidence] IS NOT NULL AND [PurchaseEvidence] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ReceiptContingencyScopes_AspNetUsers_AuthorizedBy",
                        column: x => x.AuthorizedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptContingencyScopes_AspNetUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptContingencyScopes_OperationResults_AuthorizationOperationId",
                        column: x => x.AuthorizationOperationId,
                        principalTable: "OperationResults",
                        principalColumn: "OperationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContingencyReferences_ImportedBy",
                table: "ContingencyReferences",
                column: "ImportedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ContingencyReferences_ReceiptId",
                table: "ContingencyReferences",
                column: "ReceiptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContingencyReferences_ReconciledOperationId",
                table: "ContingencyReferences",
                column: "ReconciledOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptContingencyScopes_AuthorizationOperationId",
                table: "ReceiptContingencyScopes",
                column: "AuthorizationOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptContingencyScopes_AuthorizedBy",
                table: "ReceiptContingencyScopes",
                column: "AuthorizedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptContingencyScopes_CreatedBy",
                table: "ReceiptContingencyScopes",
                column: "CreatedBy");

            migrationBuilder.Sql("DENY UPDATE, DELETE ON dbo.ContingencyReferences TO inventory_runtime; DENY DELETE ON dbo.ReceiptContingencyScopes TO inventory_runtime;");
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER dbo.TR_ReceiptContingencyScopes_Immutable ON dbo.ReceiptContingencyScopes AFTER UPDATE AS
                BEGIN
                    IF EXISTS (SELECT 1 FROM deleted WHERE [State] = ''ReopeningAuthorized'')
                        THROW 51001, ''La autorización de reapertura es inmutable.'', 1;
                END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContingencyReferences");

            migrationBuilder.DropTable(
                name: "ReceiptContingencyScopes");
        }
    }
}
