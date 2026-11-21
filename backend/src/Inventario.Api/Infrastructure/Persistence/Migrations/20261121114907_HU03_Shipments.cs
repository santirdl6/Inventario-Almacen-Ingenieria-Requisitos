using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventario.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HU03_Shipments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Shipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipments", x => x.Id);
                    table.CheckConstraint("CK_Shipments_OperationId", "([State] = N'Confirmed' AND [OperationId] IS NOT NULL) OR ([State] = N'Draft' AND [OperationId] IS NULL)");
                    table.CheckConstraint("CK_Shipments_State", "[State] IN (N'Draft', N'Confirmed')");
                    table.ForeignKey(
                        name: "FK_Shipments_CustomerOrders_CustomerOrderId",
                        column: x => x.CustomerOrderId,
                        principalTable: "CustomerOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentLines", x => x.Id);
                    table.CheckConstraint("CK_ShipmentLines_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_ShipmentLines_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentLines_CustomerOrderLines_CustomerOrderLineId",
                        column: x => x.CustomerOrderLineId,
                        principalTable: "CustomerOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentLines_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentLines_Lots_ArticleId_LotId",
                        columns: x => new { x.ArticleId, x.LotId },
                        principalTable: "Lots",
                        principalColumns: new[] { "ArticleId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentLines_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentReservationConsumptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationAllocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentReservationConsumptions", x => x.Id);
                    table.CheckConstraint("CK_ShipmentReservationConsumptions_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_ShipmentReservationConsumptions_ReservationAllocations_ReservationAllocationId",
                        column: x => x.ReservationAllocationId,
                        principalTable: "ReservationAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentReservationConsumptions_ShipmentLines_ShipmentLineId",
                        column: x => x.ShipmentLineId,
                        principalTable: "ShipmentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentLines_ArticleId_LotId",
                table: "ShipmentLines",
                columns: new[] { "ArticleId", "LotId" });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentLines_CustomerOrderLineId",
                table: "ShipmentLines",
                column: "CustomerOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentLines_LocationId",
                table: "ShipmentLines",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "UX_ShipmentLines_Shipment_Bucket",
                table: "ShipmentLines",
                columns: new[] { "ShipmentId", "CustomerOrderLineId", "LocationId", "LotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentReservationConsumptions_ReservationAllocationId",
                table: "ShipmentReservationConsumptions",
                column: "ReservationAllocationId");

            migrationBuilder.CreateIndex(
                name: "UX_ShipmentReservationConsumptions_Line_Allocation",
                table: "ShipmentReservationConsumptions",
                columns: new[] { "ShipmentLineId", "ReservationAllocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_CustomerOrder",
                table: "Shipments",
                columns: new[] { "CustomerOrderId", "RecordedAt", "Id" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "UX_Shipments_OperationId",
                table: "Shipments",
                column: "OperationId",
                unique: true,
                filter: "[OperationId] IS NOT NULL");

            // A mano (research D-06; decisión M3 de B): los triggers solo hacen THROW, sin ROLLBACK
            // explícito, para no cerrar la transacción del ejecutor (error 3609). Q1 permite borrar
            // borradores, así que Shipments y ShipmentLines no llevan DENY DELETE.
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.TR_Shipments_ConfirmedImmutable ON dbo.Shipments AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted WHERE [State] = N'Confirmed')
                    BEGIN
                        THROW 51301, N'Una salida confirmada no se puede modificar ni eliminar.', 1;
                    END
                    -- Complemento (iteración del 2026-11-21): al pasar a Confirmed, ningún consumo ya
                    -- insertado en sus líneas puede pertenecer a otra operación.
                    IF EXISTS (
                        SELECT 1
                        FROM inserted AS confirmed
                        JOIN deleted AS previous ON previous.Id = confirmed.Id
                        JOIN dbo.ShipmentLines AS line ON line.ShipmentId = confirmed.Id
                        JOIN dbo.ShipmentReservationConsumptions AS consumption ON consumption.ShipmentLineId = line.Id
                        WHERE previous.[State] = N'Draft' AND confirmed.[State] = N'Confirmed'
                          AND consumption.OperationId <> confirmed.OperationId)
                    BEGIN
                        THROW 51302, N'La salida tiene consumos de reserva de otra operación.', 1;
                    END
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.TR_ShipmentLines_ConfirmedImmutable ON dbo.ShipmentLines AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1
                        FROM (SELECT ShipmentId FROM inserted UNION ALL SELECT ShipmentId FROM deleted) AS changed
                        JOIN dbo.Shipments AS shipment ON shipment.Id = changed.ShipmentId
                        WHERE shipment.[State] = N'Confirmed')
                    BEGIN
                        THROW 51303, N'Las líneas de una salida confirmada no se pueden añadir, modificar ni eliminar.', 1;
                    END
                END
                """);
            // Consumos ligados a la operación que confirma (D-06, iteración del 2026-11-21): en cualquier
            // orden de sentencias de la confirmación, la cabecera está en Draft o ya es Confirmed con la
            // misma operación; un consumo posterior lleva otra operación y se rechaza aquí.
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.TR_ShipmentReservationConsumptions_ConfirmingOperation ON dbo.ShipmentReservationConsumptions AFTER INSERT AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1
                        FROM inserted AS consumption
                        JOIN dbo.ShipmentLines AS line ON line.Id = consumption.ShipmentLineId
                        JOIN dbo.Shipments AS shipment ON shipment.Id = line.ShipmentId
                        WHERE shipment.[State] = N'Confirmed' AND shipment.OperationId <> consumption.OperationId)
                    BEGIN
                        THROW 51304, N'No se pueden añadir consumos de reserva a una salida ya confirmada.', 1;
                    END
                END
                """);
            // Append-only (D-08): permiso negado al rol inventory_runtime, como StockEntries.
            migrationBuilder.Sql("DENY UPDATE, DELETE ON OBJECT::dbo.ShipmentReservationConsumptions TO inventory_runtime;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Orden inverso de la parte manual; el DENY desaparece con la tabla.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS dbo.TR_ShipmentReservationConsumptions_ConfirmingOperation;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS dbo.TR_ShipmentLines_ConfirmedImmutable;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS dbo.TR_Shipments_ConfirmedImmutable;");

            migrationBuilder.DropTable(
                name: "ShipmentReservationConsumptions");

            migrationBuilder.DropTable(
                name: "ShipmentLines");

            migrationBuilder.DropTable(
                name: "Shipments");
        }
    }
}
