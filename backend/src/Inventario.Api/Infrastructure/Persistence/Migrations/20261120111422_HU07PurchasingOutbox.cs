using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventario.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HU07PurchasingOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "PurchaseOrderNumbers");

            migrationBuilder.CreateTable(
                name: "PurchaseOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValueSql: "CONCAT('PO-', FORMAT(NEXT VALUE FOR [PurchaseOrderNumbers], '00000000'))"),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false),
                    ExpectedDeliveryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    State = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    SendStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevisionStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrders", x => x.Id);
                    table.CheckConstraint("CK_PurchaseOrders_Currency", "LEN([Currency])=3 AND [Currency] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'");
                    table.CheckConstraint("CK_PurchaseOrders_SendStatus", "[SendStatus] IN ('None','Pending','Accepted','Failed')");
                    table.CheckConstraint("CK_PurchaseOrders_Snapshot", "ISJSON([SupplierSnapshot])=1");
                    table.CheckConstraint("CK_PurchaseOrders_State", "[State] IN ('Draft','Sent','PartiallyReceived','Received')");
                    table.CheckConstraint("CK_PurchaseOrders_StateSend", "([SendStatus] IN ('None','Pending','Failed') AND [State]='Draft' AND [SentAt] IS NULL) OR ([SendStatus]='Accepted' AND [State] IN ('Sent','PartiallyReceived','Received') AND [SentAt] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_AspNetUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderAudit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Before = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    After = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderAudit", x => x.Id);
                    table.CheckConstraint("CK_PurchaseOrderAudit_Action", "[Action] IN ('Create','Update','RequestSend','RetrySend')");
                    table.CheckConstraint("CK_PurchaseOrderAudit_After", "[After] IS NULL OR ISJSON([After], VALUE)=1");
                    table.CheckConstraint("CK_PurchaseOrderAudit_Before", "[Before] IS NULL OR ISJSON([Before], VALUE)=1");
                    table.ForeignKey(
                        name: "FK_PurchaseOrderAudit_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderAudit_OperationResults_OperationId",
                        column: x => x.OperationId,
                        principalTable: "OperationResults",
                        principalColumn: "OperationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderAudit_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequiredPermission = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SenderEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    MessageId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    DocumentSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DocumentBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    MimeBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    DocumentName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MediaType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DocumentHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    MessageHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    TemplateVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ReviewedToken = table.Column<string>(type: "nvarchar(44)", maxLength: 44, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderDeliveries", x => x.Id);
                    table.CheckConstraint("CK_PurchaseOrderDeliveries_Contacts", "LEN([RecipientEmail])>0 AND LEN([SenderEmail])>0 AND CHARINDEX(CHAR(13),[RecipientEmail]+[SenderEmail])=0 AND CHARINDEX(CHAR(10),[RecipientEmail]+[SenderEmail])=0");
                    table.CheckConstraint("CK_PurchaseOrderDeliveries_Permission", "[RequiredPermission]='Purchasing.Send'");
                    table.CheckConstraint("CK_PurchaseOrderDeliveries_Snapshot", "ISJSON([DocumentSnapshot])=1");
                    table.ForeignKey(
                        name: "FK_PurchaseOrderDeliveries_AspNetUsers_RequestedBy",
                        column: x => x.RequestedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderDeliveries_OperationResults_RequestOperationId",
                        column: x => x.RequestOperationId,
                        principalTable: "OperationResults",
                        principalColumn: "OperationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderDeliveries_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArticleSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OrderedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValue: 0m),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpectedDeliveryDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderLines", x => x.Id);
                    table.CheckConstraint("CK_PurchaseOrderLines_Position", "[Position]>0");
                    table.CheckConstraint("CK_PurchaseOrderLines_Price", "[UnitPrice]>=0");
                    table.CheckConstraint("CK_PurchaseOrderLines_Quantity", "[OrderedQuantity]>0 AND [ReceivedQuantity]>=0 AND [ReceivedQuantity]<=[OrderedQuantity]");
                    table.CheckConstraint("CK_PurchaseOrderLines_Snapshot", "ISJSON([ArticleSnapshot])=1");
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ActiveAuthorizationOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorizedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Cycle = table.Column<int>(type: "int", nullable: false),
                    CycleAttempts = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LeaseOwner = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseGeneration = table.Column<long>(type: "bigint", nullable: false),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CurrentAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FinishOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LastErrorSummary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AcceptanceUncertain = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                    table.CheckConstraint("CK_OutboxMessages_Accepted", "([State]='Sent' AND [SentAt] IS NOT NULL) OR ([State]<>'Sent' AND [SentAt] IS NULL)");
                    table.CheckConstraint("CK_OutboxMessages_Counters", "[Cycle]>=1 AND [CycleAttempts] BETWEEN 0 AND 5 AND [Attempts]>=[CycleAttempts] AND [LeaseGeneration]>=0");
                    table.CheckConstraint("CK_OutboxMessages_Lease", "([State]='Processing' AND [LeaseOwner] IS NOT NULL AND [LeaseUntil] IS NOT NULL AND [CurrentAttemptId] IS NOT NULL AND [FinishOperationId] IS NOT NULL) OR ([State]<>'Processing' AND [LeaseOwner] IS NULL AND [LeaseUntil] IS NULL AND [CurrentAttemptId] IS NULL AND [FinishOperationId] IS NULL)");
                    table.CheckConstraint("CK_OutboxMessages_State", "[State] IN ('Pending','Processing','Sent','Failed') AND [Type]='PurchaseOrderEmail'");
                    table.ForeignKey(
                        name: "FK_OutboxMessages_AspNetUsers_AuthorizedBy",
                        column: x => x.AuthorizedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutboxMessages_OperationResults_ActiveAuthorizationOperationId",
                        column: x => x.ActiveAuthorizationOperationId,
                        principalTable: "OperationResults",
                        principalColumn: "OperationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutboxMessages_PurchaseOrderDeliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "PurchaseOrderDeliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutboxMessages_PurchaseOrders_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseDeliveryEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseGeneration = table.Column<long>(type: "bigint", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorizationOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExecutionKind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    WorkerInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ObservedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseDeliveryEvents", x => x.Id);
                    table.CheckConstraint("CK_PurchaseDeliveryEvents_Execution", "[ExecutionKind] IN ('Human','Worker')");
                    table.CheckConstraint("CK_PurchaseDeliveryEvents_Generation", "[LeaseGeneration] IS NULL OR [LeaseGeneration]>=0");
                    table.CheckConstraint("CK_PurchaseDeliveryEvents_Kind", "[Kind] IN ('Requested','Retried','Claimed','LeaseExpired','Accepted','TransientFailure','PermanentFailure','Uncertain','LateObservation')");
                    table.CheckConstraint("CK_PurchaseDeliveryEvents_Outcome", "ISJSON([Outcome], VALUE)=1");
                    table.ForeignKey(
                        name: "FK_PurchaseDeliveryEvents_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseDeliveryEvents_OperationResults_AuthorizationOperationId",
                        column: x => x.AuthorizationOperationId,
                        principalTable: "OperationResults",
                        principalColumn: "OperationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseDeliveryEvents_OperationResults_OperationId",
                        column: x => x.OperationId,
                        principalTable: "OperationResults",
                        principalColumn: "OperationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseDeliveryEvents_PurchaseOrderDeliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "PurchaseOrderDeliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ActiveAuthorizationOperationId",
                table: "OutboxMessages",
                column: "ActiveAuthorizationOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_AuthorizedBy",
                table: "OutboxMessages",
                column: "AuthorizedBy");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_DeliveryId",
                table: "OutboxMessages",
                column: "DeliveryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_DocumentId",
                table: "OutboxMessages",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_State_LeaseUntil",
                table: "OutboxMessages",
                columns: new[] { "State", "LeaseUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_State_NextAttemptAt",
                table: "OutboxMessages",
                columns: new[] { "State", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseDeliveryEvents_ActorId",
                table: "PurchaseDeliveryEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseDeliveryEvents_AuthorizationOperationId",
                table: "PurchaseDeliveryEvents",
                column: "AuthorizationOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseDeliveryEvents_DeliveryId_RecordedAt",
                table: "PurchaseDeliveryEvents",
                columns: new[] { "DeliveryId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseDeliveryEvents_OperationId_Kind_DeliveryId",
                table: "PurchaseDeliveryEvents",
                columns: new[] { "OperationId", "Kind", "DeliveryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderAudit_ActorId",
                table: "PurchaseOrderAudit",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderAudit_OperationId_PurchaseOrderId_Action",
                table: "PurchaseOrderAudit",
                columns: new[] { "OperationId", "PurchaseOrderId", "Action" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderAudit_PurchaseOrderId_RecordedAt",
                table: "PurchaseOrderAudit",
                columns: new[] { "PurchaseOrderId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderDeliveries_MessageId",
                table: "PurchaseOrderDeliveries",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderDeliveries_PurchaseOrderId",
                table: "PurchaseOrderDeliveries",
                column: "PurchaseOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderDeliveries_RequestedBy",
                table: "PurchaseOrderDeliveries",
                column: "RequestedBy");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderDeliveries_RequestOperationId",
                table: "PurchaseOrderDeliveries",
                column: "RequestOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_ArticleId",
                table: "PurchaseOrderLines",
                column: "ArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_PurchaseOrderId_Position",
                table: "PurchaseOrderLines",
                columns: new[] { "PurchaseOrderId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_CreatedAt_Number_Id",
                table: "PurchaseOrders",
                columns: new[] { "CreatedAt", "Number", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_CreatedBy",
                table: "PurchaseOrders",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_Number",
                table: "PurchaseOrders",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_SupplierId_State",
                table: "PurchaseOrders",
                columns: new[] { "SupplierId", "State" });

            migrationBuilder.Sql("""
                DENY UPDATE, DELETE ON OBJECT::dbo.PurchaseOrderDeliveries TO inventory_runtime;
                DENY UPDATE, DELETE ON OBJECT::dbo.PurchaseOrderAudit TO inventory_runtime;
                DENY UPDATE, DELETE ON OBJECT::dbo.PurchaseDeliveryEvents TO inventory_runtime;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "PurchaseDeliveryEvents");

            migrationBuilder.DropTable(
                name: "PurchaseOrderAudit");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLines");

            migrationBuilder.DropTable(
                name: "PurchaseOrderDeliveries");

            migrationBuilder.DropTable(
                name: "PurchaseOrders");

            migrationBuilder.DropSequence(
                name: "PurchaseOrderNumbers");
        }
    }
}
