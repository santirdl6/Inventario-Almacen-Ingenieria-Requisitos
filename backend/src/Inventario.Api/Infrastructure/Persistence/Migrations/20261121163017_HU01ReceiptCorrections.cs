using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventario.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HU01ReceiptCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReceiptCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceiptCorrections_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptCorrections_OperationResults_OperationId",
                        column: x => x.OperationId,
                        principalTable: "OperationResults",
                        principalColumn: "OperationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptCorrections_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptCorrections_StockMovements_MovementId",
                        column: x => x.MovementId,
                        principalTable: "StockMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptCorrections_StockMovements_OriginalMovementId",
                        column: x => x.OriginalMovementId,
                        principalTable: "StockMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptCorrectionLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorrectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptAllocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptCorrectionLines", x => x.Id);
                    table.CheckConstraint("CK_ReceiptCorrectionLines_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_ReceiptCorrectionLines_ReceiptAllocations_ReceiptAllocationId",
                        column: x => x.ReceiptAllocationId,
                        principalTable: "ReceiptAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptCorrectionLines_ReceiptCorrections_CorrectionId",
                        column: x => x.CorrectionId,
                        principalTable: "ReceiptCorrections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCorrectionLines_CorrectionId_ReceiptAllocationId",
                table: "ReceiptCorrectionLines",
                columns: new[] { "CorrectionId", "ReceiptAllocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCorrectionLines_ReceiptAllocationId",
                table: "ReceiptCorrectionLines",
                column: "ReceiptAllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCorrections_ActorId",
                table: "ReceiptCorrections",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCorrections_MovementId",
                table: "ReceiptCorrections",
                column: "MovementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCorrections_OperationId",
                table: "ReceiptCorrections",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCorrections_OriginalMovementId",
                table: "ReceiptCorrections",
                column: "OriginalMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCorrections_ReceiptId",
                table: "ReceiptCorrections",
                column: "ReceiptId");

            migrationBuilder.Sql("DENY UPDATE, DELETE ON dbo.ReceiptCorrections TO inventory_runtime; DENY UPDATE, DELETE ON dbo.ReceiptCorrectionLines TO inventory_runtime;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReceiptCorrectionLines");

            migrationBuilder.DropTable(
                name: "ReceiptCorrections");
        }
    }
}
