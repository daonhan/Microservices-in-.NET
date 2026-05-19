using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Saga.Service.Infrastructure.Data.EntityFramework;

#nullable disable

namespace Saga.Service.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(SagaContext))]
    [Migration("20260518000000_AddRefundSagaState")]
    public partial class AddRefundSagaState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RefundSagaStates",
                columns: table => new
                {
                    SagaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefundAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    LastStepResult = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefundSagaStates", x => x.SagaId);
                    table.ForeignKey(
                        name: "FK_RefundSagaStates_SagaInstances_SagaId",
                        column: x => x.SagaId,
                        principalTable: "SagaInstances",
                        principalColumn: "SagaId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RefundSagaStates_OrderId",
                table: "RefundSagaStates",
                column: "OrderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RefundSagaStates");
        }
    }
}
