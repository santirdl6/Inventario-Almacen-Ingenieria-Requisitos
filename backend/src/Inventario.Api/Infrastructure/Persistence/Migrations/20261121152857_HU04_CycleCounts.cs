using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventario.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HU04_CycleCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CycleCounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CountCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    State = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SubmittedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AdjustmentMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CycleCounts", x => x.Id);
                    table.CheckConstraint("CK_CycleCounts_CancelReason", "[CancelReason] IS NULL OR LEN(LTRIM(RTRIM([CancelReason]))) > 0");
                    table.CheckConstraint("CK_CycleCounts_Pairs", "(([StartedBy] IS NULL AND [StartedAt] IS NULL) OR ([StartedBy] IS NOT NULL AND [StartedAt] IS NOT NULL)) AND (([SubmittedBy] IS NULL AND [SubmittedAt] IS NULL) OR ([SubmittedBy] IS NOT NULL AND [SubmittedAt] IS NOT NULL)) AND (([ApprovedBy] IS NULL AND [ApprovedAt] IS NULL) OR ([ApprovedBy] IS NOT NULL AND [ApprovedAt] IS NOT NULL)) AND (([CancelledBy] IS NULL AND [CancelledAt] IS NULL AND [CancelReason] IS NULL) OR ([CancelledBy] IS NOT NULL AND [CancelledAt] IS NOT NULL AND [CancelReason] IS NOT NULL))");
                    table.CheckConstraint("CK_CycleCounts_State", "([State] = N'Draft' AND [StartedBy] IS NULL AND [SubmittedBy] IS NULL AND [ApprovedBy] IS NULL AND [CancelledBy] IS NULL AND [OperationId] IS NULL) OR ([State] = N'Counting' AND [StartedBy] IS NOT NULL AND [SubmittedBy] IS NULL AND [ApprovedBy] IS NULL AND [CancelledBy] IS NULL AND [OperationId] IS NULL) OR ([State] = N'AwaitingApproval' AND [StartedBy] IS NOT NULL AND [SubmittedBy] IS NOT NULL AND [ApprovedBy] IS NULL AND [CancelledBy] IS NULL AND [OperationId] IS NULL) OR ([State] = N'Confirmed' AND [StartedBy] IS NOT NULL AND [SubmittedBy] IS NOT NULL AND [ApprovedBy] IS NOT NULL AND [CancelledBy] IS NULL AND [OperationId] IS NOT NULL) OR ([State] = N'Cancelled' AND [ApprovedBy] IS NULL AND [CancelledBy] IS NOT NULL AND [OperationId] IS NOT NULL AND [AdjustmentMovementId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_CycleCounts_AspNetUsers_ApprovedBy",
                        column: x => x.ApprovedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CycleCounts_AspNetUsers_CancelledBy",
                        column: x => x.CancelledBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CycleCounts_AspNetUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CycleCounts_AspNetUsers_StartedBy",
                        column: x => x.StartedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CycleCounts_AspNetUsers_SubmittedBy",
                        column: x => x.SubmittedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CycleCounts_StockMovements_AdjustmentMovementId",
                        column: x => x.AdjustmentMovementId,
                        principalTable: "StockMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CountLocations",
                columns: table => new
                {
                    CountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsFrozen = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CountLocations", x => new { x.CountId, x.LocationId });
                    table.ForeignKey(
                        name: "FK_CountLocations_CycleCounts_CountId",
                        column: x => x.CountId,
                        principalTable: "CycleCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CountLocations_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CountLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LotCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsFound = table.Column<bool>(type: "bit", nullable: false),
                    TheoreticalPhysical = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    CountedPhysical = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    Difference = table.Column<decimal>(type: "decimal(18,3)", nullable: true, computedColumnSql: "[CountedPhysical] - [TheoreticalPhysical]", stored: true),
                    CountedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CountedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReasonBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrectsShipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CountLines", x => x.Id);
                    table.CheckConstraint("CK_CountLines_Found", "([IsFound] = 1 AND [TheoreticalPhysical] = 0 AND [CountedPhysical] > 0) OR ([IsFound] = 0 AND [LotId] IS NOT NULL)");
                    table.CheckConstraint("CK_CountLines_Pairs", "(([CountedPhysical] IS NULL AND [CountedBy] IS NULL AND [CountedAt] IS NULL) OR ([CountedPhysical] IS NOT NULL AND [CountedBy] IS NOT NULL AND [CountedAt] IS NOT NULL)) AND (([Reason] IS NULL AND [ReasonBy] IS NULL) OR ([Reason] IS NOT NULL AND [ReasonBy] IS NOT NULL))");
                    table.CheckConstraint("CK_CountLines_Quantities", "[TheoreticalPhysical] >= 0 AND ([CountedPhysical] IS NULL OR [CountedPhysical] >= 0)");
                    table.CheckConstraint("CK_CountLines_Shipment", "([CorrectsShipmentId] IS NULL AND [Reason] IS NULL) OR ([CountedPhysical] IS NOT NULL AND [CountedPhysical] <> [TheoreticalPhysical])");
                    table.ForeignKey(
                        name: "FK_CountLines_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CountLines_AspNetUsers_CountedBy",
                        column: x => x.CountedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CountLines_AspNetUsers_ReasonBy",
                        column: x => x.ReasonBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CountLines_CountLocations_CountId_LocationId",
                        columns: x => new { x.CountId, x.LocationId },
                        principalTable: "CountLocations",
                        principalColumns: new[] { "CountId", "LocationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CountLines_CycleCounts_CountId",
                        column: x => x.CountId,
                        principalTable: "CycleCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CountLines_Lots_ArticleId_LotId",
                        columns: x => new { x.ArticleId, x.LotId },
                        principalTable: "Lots",
                        principalColumns: new[] { "ArticleId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CountLines_Shipments_CorrectsShipmentId",
                        column: x => x.CorrectsShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CountLines_ArticleId_LotId",
                table: "CountLines",
                columns: new[] { "ArticleId", "LotId" });

            migrationBuilder.CreateIndex(
                name: "IX_CountLines_Bucket",
                table: "CountLines",
                columns: new[] { "ArticleId", "LocationId", "LotId" });

            migrationBuilder.CreateIndex(
                name: "IX_CountLines_CorrectsShipmentId",
                table: "CountLines",
                column: "CorrectsShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CountLines_CountedBy",
                table: "CountLines",
                column: "CountedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CountLines_CountId_LocationId",
                table: "CountLines",
                columns: new[] { "CountId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_CountLines_ReasonBy",
                table: "CountLines",
                column: "ReasonBy");

            migrationBuilder.CreateIndex(
                name: "UX_CountLines_Bucket",
                table: "CountLines",
                columns: new[] { "CountId", "ArticleId", "LocationId", "LotCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CountLocations_Location",
                table: "CountLocations",
                columns: new[] { "LocationId", "IsFrozen" });

            migrationBuilder.CreateIndex(
                name: "UX_CountLocations_Frozen",
                table: "CountLocations",
                column: "LocationId",
                unique: true,
                filter: "[IsFrozen] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCounts_AdjustmentMovementId",
                table: "CycleCounts",
                column: "AdjustmentMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCounts_ApprovedBy",
                table: "CycleCounts",
                column: "ApprovedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCounts_CancelledBy",
                table: "CycleCounts",
                column: "CancelledBy");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCounts_CreatedAt",
                table: "CycleCounts",
                columns: new[] { "CreatedAt", "Id" },
                descending: new[] { true, false });

            migrationBuilder.CreateIndex(
                name: "IX_CycleCounts_CreatedBy",
                table: "CycleCounts",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCounts_StartedBy",
                table: "CycleCounts",
                column: "StartedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCounts_SubmittedBy",
                table: "CycleCounts",
                column: "SubmittedBy");

            migrationBuilder.CreateIndex(
                name: "UX_CycleCounts_CountCode",
                table: "CycleCounts",
                column: "CountCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CycleCounts_OperationId",
                table: "CycleCounts",
                column: "OperationId",
                unique: true,
                filter: "[OperationId] IS NOT NULL");

            // A mano (research D-07; decisión M3 de HU-03): los triggers solo hacen THROW, sin ROLLBACK
            // explícito, para no cerrar la transacción del ejecutor (error 3609). Confirmar y cancelar no
            // modifican líneas, así que el orden de sentencias de EF no importa.
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.TR_CycleCounts_ClosedImmutable ON dbo.CycleCounts AFTER UPDATE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted WHERE [State] IN (N'Confirmed', N'Cancelled'))
                    BEGIN
                        THROW 51401, N'Un conteo cerrado no se puede modificar.', 1;
                    END
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.TR_CountLines_OpenCount ON dbo.CountLines AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1
                        FROM (SELECT CountId FROM inserted UNION ALL SELECT CountId FROM deleted) AS changed
                        JOIN dbo.CycleCounts AS header ON header.Id = changed.CountId
                        WHERE header.[State] IN (N'Confirmed', N'Cancelled'))
                    BEGIN
                        THROW 51402, N'Las líneas de un conteo cerrado no se pueden añadir, modificar ni eliminar.', 1;
                    END
                    IF EXISTS (
                        SELECT 1 FROM deleted AS removed
                        WHERE removed.IsFound = 0 AND NOT EXISTS (SELECT 1 FROM inserted WHERE inserted.Id = removed.Id))
                    BEGIN
                        THROW 51403, N'Las líneas esperadas de un conteo no se pueden eliminar.', 1;
                    END
                    IF EXISTS (
                        SELECT 1
                        FROM inserted AS updated
                        JOIN deleted AS previous ON previous.Id = updated.Id
                        JOIN dbo.CycleCounts AS header ON header.Id = updated.CountId
                        WHERE header.[State] = N'AwaitingApproval'
                          AND (updated.CountedPhysical <> previous.CountedPhysical
                               OR (updated.CountedPhysical IS NULL AND previous.CountedPhysical IS NOT NULL)
                               OR (updated.CountedPhysical IS NOT NULL AND previous.CountedPhysical IS NULL)))
                    BEGIN
                        THROW 51404, N'El contado no cambia con el conteo pendiente de aprobación.', 1;
                    END
                END
                """);
            // Alcance (D-07): insertar o borrar solo en Draft (51405); actualizar solo IsFrozen (51406);
            // 0 → 1 en Draft o Counting y 1 → 0 desde Counting (51407). Una cabecera cerrada no vuelve
            // atrás: una ubicación liberada no se vuelve a congelar con ese conteo.
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.TR_CountLocations_Scope ON dbo.CountLocations AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
                       AND (UPDATE(CountId) OR UPDATE(LocationId))
                    BEGIN
                        THROW 51406, N'En el alcance de un conteo solo cambia la congelación.', 1;
                    END
                    IF EXISTS (
                        SELECT 1
                        FROM (
                            SELECT added.CountId FROM inserted AS added
                            WHERE NOT EXISTS (SELECT 1 FROM deleted AS d WHERE d.CountId = added.CountId AND d.LocationId = added.LocationId)
                            UNION ALL
                            SELECT removed.CountId FROM deleted AS removed
                            WHERE NOT EXISTS (SELECT 1 FROM inserted AS i WHERE i.CountId = removed.CountId AND i.LocationId = removed.LocationId)
                        ) AS changed
                        JOIN dbo.CycleCounts AS header ON header.Id = changed.CountId
                        WHERE header.[State] <> N'Draft')
                    BEGIN
                        THROW 51405, N'El alcance de un conteo solo se edita en borrador.', 1;
                    END
                    IF EXISTS (
                        SELECT 1
                        FROM inserted AS updated
                        JOIN deleted AS previous ON previous.CountId = updated.CountId AND previous.LocationId = updated.LocationId
                        JOIN dbo.CycleCounts AS header ON header.Id = updated.CountId
                        WHERE (previous.IsFrozen = 0 AND updated.IsFrozen = 1 AND header.[State] NOT IN (N'Draft', N'Counting'))
                           OR (previous.IsFrozen = 1 AND updated.IsFrozen = 0 AND header.[State] = N'Draft'))
                    BEGIN
                        THROW 51407, N'La congelación del alcance no admite ese cambio en el estado del conteo.', 1;
                    END
                END
                """);
            // El borrador se cancela, no se elimina (D-02): sin DELETE para la conexión de ejecución.
            migrationBuilder.Sql("DENY DELETE ON OBJECT::dbo.CycleCounts TO inventory_runtime;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Orden inverso de la parte manual; el DENY desaparece con la tabla.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS dbo.TR_CountLocations_Scope;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS dbo.TR_CountLines_OpenCount;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS dbo.TR_CycleCounts_ClosedImmutable;");

            migrationBuilder.DropTable(
                name: "CountLines");

            migrationBuilder.DropTable(
                name: "CountLocations");

            migrationBuilder.DropTable(
                name: "CycleCounts");
        }
    }
}
