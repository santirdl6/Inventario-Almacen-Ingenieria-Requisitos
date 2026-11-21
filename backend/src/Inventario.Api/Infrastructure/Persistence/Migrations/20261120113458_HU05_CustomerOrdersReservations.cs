using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventario.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HU05_CustomerOrdersReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalReference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    CustomerReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerOrders", x => x.Id);
                    table.CheckConstraint("CK_CustomerOrders_CustomerReference_NotBlank", "LEN(LTRIM(RTRIM([CustomerReference]))) > 0");
                    table.CheckConstraint("CK_CustomerOrders_ExternalReference_NotBlank", "LEN(LTRIM(RTRIM([ExternalReference]))) > 0");
                    table.CheckConstraint("CK_CustomerOrders_State", "[State] IN (N'Open', N'Fulfilled', N'Cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "CustomerOrderLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderedQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    ShippedQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerOrderLines", x => x.Id);
                    table.CheckConstraint("CK_CustomerOrderLines_Ordered", "[OrderedQuantity] > 0");
                    table.CheckConstraint("CK_CustomerOrderLines_Shipped", "[ShippedQuantity] >= 0 AND [ShippedQuantity] <= [OrderedQuantity]");
                    table.ForeignKey(
                        name: "FK_CustomerOrderLines_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerOrderLines_CustomerOrders_CustomerOrderId",
                        column: x => x.CustomerOrderId,
                        principalTable: "CustomerOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                    table.CheckConstraint("CK_Reservations_State", "[State] IN (N'Active', N'Consumed', N'Released')");
                    table.ForeignKey(
                        name: "FK_Reservations_CustomerOrderLines_CustomerOrderLineId",
                        column: x => x.CustomerOrderLineId,
                        principalTable: "CustomerOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReservationAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllocatedQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    ConsumedQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    ReleasedQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    RemainingQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false, computedColumnSql: "[AllocatedQuantity] - [ConsumedQuantity] - [ReleasedQuantity]", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationAllocations", x => x.Id);
                    table.CheckConstraint("CK_ReservationAllocations_Allocated", "[AllocatedQuantity] > 0");
                    table.CheckConstraint("CK_ReservationAllocations_Consumed", "[ConsumedQuantity] >= 0");
                    table.CheckConstraint("CK_ReservationAllocations_Released", "[ReleasedQuantity] >= 0");
                    table.CheckConstraint("CK_ReservationAllocations_Remaining", "[AllocatedQuantity] - [ConsumedQuantity] - [ReleasedQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_ReservationAllocations_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReservationAllocations_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReservationAllocations_Lots_ArticleId_LotId",
                        columns: x => new { x.ArticleId, x.LotId },
                        principalTable: "Lots",
                        principalColumns: new[] { "ArticleId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReservationAllocations_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerOrderLines_ArticleId",
                table: "CustomerOrderLines",
                column: "ArticleId");

            migrationBuilder.CreateIndex(
                name: "UX_CustomerOrderLines_Order_Article",
                table: "CustomerOrderLines",
                columns: new[] { "CustomerOrderId", "ArticleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CustomerOrders_ExternalReference",
                table: "CustomerOrders",
                column: "ExternalReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReservationAllocations_ArticleId_LotId",
                table: "ReservationAllocations",
                columns: new[] { "ArticleId", "LotId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationAllocations_Bucket",
                table: "ReservationAllocations",
                columns: new[] { "ArticleId", "LocationId", "LotId" })
                .Annotation("SqlServer:Include", new[] { "RemainingQuantity" });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationAllocations_LocationId",
                table: "ReservationAllocations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "UX_ReservationAllocations_Reservation_Bucket",
                table: "ReservationAllocations",
                columns: new[] { "ReservationId", "ArticleId", "LocationId", "LotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_CustomerOrderLineId",
                table: "Reservations",
                column: "CustomerOrderLineId");

            // HU-05 (research D-14, P-02): en esta entrega nada de esto se borra. Mismo patrón que
            // H0_StockCore (StockBalances): permiso negado al rol inventory_runtime, no al login.
            // Down elimina las tablas y, con ellas, estos permisos.
            migrationBuilder.Sql("""
                DENY DELETE ON OBJECT::dbo.CustomerOrders TO inventory_runtime;
                DENY DELETE ON OBJECT::dbo.CustomerOrderLines TO inventory_runtime;
                DENY DELETE ON OBJECT::dbo.Reservations TO inventory_runtime;
                DENY DELETE ON OBJECT::dbo.ReservationAllocations TO inventory_runtime;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReservationAllocations");

            migrationBuilder.DropTable(
                name: "Reservations");

            migrationBuilder.DropTable(
                name: "CustomerOrderLines");

            migrationBuilder.DropTable(
                name: "CustomerOrders");
        }
    }
}
