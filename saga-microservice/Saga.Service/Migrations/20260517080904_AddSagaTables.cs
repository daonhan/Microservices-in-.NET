using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saga.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddSagaTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SagaInstances",
                columns: table => new
                {
                    SagaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SagaType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CurrentStep = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NextTimeoutAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RetryCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SagaInstances", x => x.SagaId);
                });

            migrationBuilder.CreateTable(
                name: "OrderSagaStates",
                columns: table => new
                {
                    SagaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ShipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastStepResult = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderSagaStates", x => x.SagaId);
                    table.ForeignKey(
                        name: "FK_OrderSagaStates_SagaInstances_SagaId",
                        column: x => x.SagaId,
                        principalTable: "SagaInstances",
                        principalColumn: "SagaId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SagaTransitions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SagaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromStep = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ToStep = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TriggerMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TriggerKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SagaTransitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SagaTransitions_SagaInstances_SagaId",
                        column: x => x.SagaId,
                        principalTable: "SagaInstances",
                        principalColumn: "SagaId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderSagaStates_OrderId",
                table: "OrderSagaStates",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SagaInstances_SagaType_Status_NextTimeoutAt",
                table: "SagaInstances",
                columns: new[] { "SagaType", "Status", "NextTimeoutAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SagaTransitions_SagaId",
                table: "SagaTransitions",
                column: "SagaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderSagaStates");

            migrationBuilder.DropTable(
                name: "SagaTransitions");

            migrationBuilder.DropTable(
                name: "SagaInstances");
        }
    }
}
