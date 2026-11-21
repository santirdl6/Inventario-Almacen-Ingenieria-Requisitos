using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventario.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HU01Receiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SupplierDocumentReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SupportNotApplicableReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CounterpartyReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevisionStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receipts", x => x.Id);
                    table.CheckConstraint("CK_Receipts_Confirmation", "([State] = 'Draft' AND [RecordedAt] IS NULL AND [ActorId] IS NULL AND [OperationId] IS NULL AND [MovementId] IS NULL AND [CounterpartyReference] IS NULL)\nOR ([State] = 'Confirmed' AND [RecordedAt] IS NOT NULL AND [ActorId] IS NOT NULL AND [OperationId] IS NOT NULL AND [MovementId] IS NOT NULL AND [CounterpartyReference] IS NOT NULL AND [Reason] IS NOT NULL\n    AND (([SupplierDocumentReference] IS NULL AND [SupportNotApplicableReason] IS NOT NULL) OR ([SupplierDocumentReference] IS NOT NULL AND [SupportNotApplicableReason] IS NULL)))");
                    table.ForeignKey(
                        name: "FK_Receipts_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Receipts_AspNetUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Receipts_OperationResults_OperationId",
                        column: x => x.OperationId,
                        principalTable: "OperationResults",
                        principalColumn: "OperationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Receipts_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Receipts_StockMovements_MovementId",
                        column: x => x.MovementId,
                        principalTable: "StockMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    AcceptedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    DiscrepancyReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OrderedAtConfirmation = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    ReceivedBeforeConfirmation = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    PendingAfterConfirmation = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    ArticleSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptLines", x => x.Id);
                    table.CheckConstraint("CK_ReceiptLines_Discrepancy", "[ReportedQuantity] = [AcceptedQuantity] OR ([DiscrepancyReason] IS NOT NULL AND LEN(LTRIM(RTRIM([DiscrepancyReason]))) > 0)");
                    table.CheckConstraint("CK_ReceiptLines_Quantities", "[AcceptedQuantity] >= 0 AND [AcceptedQuantity] <= [ReportedQuantity]");
                    table.CheckConstraint("CK_ReceiptLines_Snapshot", "[ArticleSnapshot] IS NULL OR ISJSON([ArticleSnapshot]) = 1");
                    table.ForeignKey(
                        name: "FK_ReceiptLines_PurchaseOrderLines_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "PurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptLines_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LotCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    LocationSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptAllocations", x => x.Id);
                    table.CheckConstraint("CK_ReceiptAllocations_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_ReceiptAllocations_Snapshot", "([LotId] IS NULL AND [LocationSnapshot] IS NULL) OR ([LotId] IS NOT NULL AND [LotCode] IS NOT NULL AND [LocationSnapshot] IS NOT NULL AND ISJSON([LocationSnapshot]) = 1)");
                    table.ForeignKey(
                        name: "FK_ReceiptAllocations_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptAllocations_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptAllocations_ReceiptLines_ReceiptLineId",
                        column: x => x.ReceiptLineId,
                        principalTable: "ReceiptLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptIncidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ObservedDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    ObservedUnit = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptIncidents", x => x.Id);
                    table.CheckConstraint("CK_ReceiptIncidents_Quantity", "[Quantity] IS NULL OR [Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_ReceiptIncidents_ReceiptLines_ReceiptLineId",
                        column: x => x.ReceiptLineId,
                        principalTable: "ReceiptLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptIncidents_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptAllocations_LocationId",
                table: "ReceiptAllocations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptAllocations_LotId",
                table: "ReceiptAllocations",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptAllocations_ReceiptLineId",
                table: "ReceiptAllocations",
                column: "ReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptIncidents_ReceiptId",
                table: "ReceiptIncidents",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptIncidents_ReceiptLineId",
                table: "ReceiptIncidents",
                column: "ReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptLines_PurchaseOrderLineId",
                table: "ReceiptLines",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptLines_ReceiptId_PurchaseOrderLineId",
                table: "ReceiptLines",
                columns: new[] { "ReceiptId", "PurchaseOrderLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_ActorId_OccurredAt",
                table: "Receipts",
                columns: new[] { "ActorId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_CreatedBy",
                table: "Receipts",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_MovementId",
                table: "Receipts",
                column: "MovementId",
                unique: true,
                filter: "[MovementId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_OperationId",
                table: "Receipts",
                column: "OperationId",
                unique: true,
                filter: "[OperationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_PurchaseOrderId_OccurredAt_Id",
                table: "Receipts",
                columns: new[] { "PurchaseOrderId", "OccurredAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReceiptAllocations");

            migrationBuilder.DropTable(
                name: "ReceiptIncidents");

            migrationBuilder.DropTable(
                name: "ReceiptLines");

            migrationBuilder.DropTable(
                name: "Receipts");
        }
    }
}
