using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventario.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class H0_StockCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    Aisle = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Rack = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Level = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                    table.CheckConstraint("CK_Locations_Aisle_NotBlank", "LEN(LTRIM(RTRIM([Aisle]))) > 0");
                    table.CheckConstraint("CK_Locations_Code_NotBlank", "LEN(LTRIM(RTRIM([Code]))) > 0");
                    table.CheckConstraint("CK_Locations_Kind", "[Kind] IN ('Storage', 'Receiving')");
                    table.CheckConstraint("CK_Locations_Level_NotBlank", "LEN(LTRIM(RTRIM([Level]))) > 0");
                    table.CheckConstraint("CK_Locations_Rack_NotBlank", "LEN(LTRIM(RTRIM([Rack]))) > 0");
                });

            migrationBuilder.CreateTable(
                name: "Lots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    IsUntracked = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lots", x => x.Id);
                    table.UniqueConstraint("AK_Lots_ArticleId_Id", x => new { x.ArticleId, x.Id });
                    table.CheckConstraint("CK_Lots_Code_NotBlank", "LEN(LTRIM(RTRIM([Code]))) > 0");
                    table.CheckConstraint("CK_Lots_Untracked_Code", "([IsUntracked] = 1 AND [Code] = 'SIN-LOTE') OR ([IsUntracked] = 0 AND [Code] <> 'SIN-LOTE')");
                    table.ForeignKey(
                        name: "FK_Lots_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OperationResults",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Resource = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ResultStatus = table.Column<int>(type: "int", nullable: false),
                    ResultBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationResults", x => x.OperationId);
                    table.ForeignKey(
                        name: "FK_OperationResults_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SourceDocumentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupportReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SupportNotApplicableReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CounterpartyReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                    table.CheckConstraint("CK_StockMovements_Reason_NotBlank", "LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_StockMovements_Support", "[SupportReference] IS NOT NULL OR [SupportNotApplicableReason] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_StockMovements_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_StockMovements_OriginalMovementId",
                        column: x => x.OriginalMovementId,
                        principalTable: "StockMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockBalances",
                columns: table => new
                {
                    ArticleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Physical = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    Reserved = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockBalances", x => new { x.ArticleId, x.LocationId, x.LotId });
                    table.CheckConstraint("CK_StockBalances_Physical", "[Physical] >= 0");
                    table.CheckConstraint("CK_StockBalances_Reserved", "[Reserved] >= 0 AND [Reserved] <= [Physical]");
                    table.ForeignKey(
                        name: "FK_StockBalances_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockBalances_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockBalances_Lots_ArticleId_LotId",
                        columns: x => new { x.ArticleId, x.LotId },
                        principalTable: "Lots",
                        principalColumns: new[] { "ArticleId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    BaseUnitSnapshot = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Effect = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PhysicalDelta = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    ReservedDelta = table.Column<decimal>(type: "decimal(18,3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockEntries", x => x.Id);
                    table.CheckConstraint("CK_StockEntries_Effect", "([Effect] IN ('Receive', 'AdjustUp') AND [PhysicalDelta] = [Quantity] AND [ReservedDelta] = 0)\nOR ([Effect] IN ('ShipAvailable', 'AdjustDown') AND [PhysicalDelta] = -[Quantity] AND [ReservedDelta] = 0)\nOR ([Effect] = 'Reserve' AND [PhysicalDelta] = 0 AND [ReservedDelta] = [Quantity])\nOR ([Effect] = 'Release' AND [PhysicalDelta] = 0 AND [ReservedDelta] = -[Quantity])\nOR ([Effect] = 'ShipReserved' AND [PhysicalDelta] = -[Quantity] AND [ReservedDelta] = -[Quantity])");
                    table.CheckConstraint("CK_StockEntries_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_StockEntries_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockEntries_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockEntries_Lots_ArticleId_LotId",
                        columns: x => new { x.ArticleId, x.LotId },
                        principalTable: "Lots",
                        principalColumns: new[] { "ArticleId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockEntries_StockMovements_MovementId",
                        column: x => x.MovementId,
                        principalTable: "StockMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Locations_Code",
                table: "Locations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lots_ArticleId_Code",
                table: "Lots",
                columns: new[] { "ArticleId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationResults_ActorId",
                table: "OperationResults",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_StockBalances_ArticleId_LotId",
                table: "StockBalances",
                columns: new[] { "ArticleId", "LotId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockBalances_LocationId_ArticleId",
                table: "StockBalances",
                columns: new[] { "LocationId", "ArticleId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockEntries_ArticleId_LocationId_LotId",
                table: "StockEntries",
                columns: new[] { "ArticleId", "LocationId", "LotId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockEntries_ArticleId_LotId",
                table: "StockEntries",
                columns: new[] { "ArticleId", "LotId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockEntries_LocationId",
                table: "StockEntries",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_StockEntries_MovementId",
                table: "StockEntries",
                column: "MovementId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ActorId",
                table: "StockMovements",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_OperationId",
                table: "StockMovements",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_OriginalMovementId",
                table: "StockMovements",
                column: "OriginalMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_SourceDocumentType_SourceDocumentId_Kind",
                table: "StockMovements",
                columns: new[] { "SourceDocumentType", "SourceDocumentId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperationResults");

            migrationBuilder.DropTable(
                name: "StockBalances");

            migrationBuilder.DropTable(
                name: "StockEntries");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropTable(
                name: "Lots");

            migrationBuilder.DropTable(
                name: "StockMovements");
        }
    }
}
