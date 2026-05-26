using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Saga.Service.Infrastructure.Data.EntityFramework;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Saga.Service.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(SagaContext))]
    [Migration("20260526100000_SeedQaPhase2_Saga")]
    public partial class SeedQaPhase2_Saga : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SagaInstances",
                columns: new[] { "SagaId", "SagaType", "CurrentStep", "Status", "CorrelationId", "CreatedAt", "UpdatedAt", "NextTimeoutAt", "RetryCount", "LastCommandId" },
                columnTypes: new[] { "uniqueidentifier", "nvarchar(64)", "nvarchar(64)", "nvarchar(32)", "uniqueidentifier", "datetime2", "datetime2", "datetime2", "int", "uniqueidentifier" },
                values: new object[]
                {
                    new Guid("e0000000-0000-0000-0000-000000000001"),
                    "Order",
                    "PaymentAuthorizing",
                    "Running",
                    new Guid("e0000000-0000-0000-0000-0000000000a1"),
                    new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc),
                    null,
                    0,
                    new Guid("e0000000-0000-0000-0000-000000000003")
                });

            migrationBuilder.InsertData(
                table: "OrderSagaStates",
                columns: new[] { "SagaId", "OrderId", "ReservationId", "PaymentId", "ShipmentId", "Amount", "CompensationOrigin", "LastStepResult" },
                columnTypes: new[] { "uniqueidentifier", "uniqueidentifier", "uniqueidentifier", "uniqueidentifier", "uniqueidentifier", "decimal(18,2)", "nvarchar(64)", "nvarchar(128)" },
                values: new object[]
                {
                    new Guid("e0000000-0000-0000-0000-000000000001"),
                    new Guid("e0000000-0000-0000-0000-000000000002"),
                    null,
                    null,
                    null,
                    null,
                    null,
                    "QaSeed"
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "OrderSagaStates",
                keyColumn: "SagaId",
                keyColumnType: "uniqueidentifier",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "SagaInstances",
                keyColumn: "SagaId",
                keyColumnType: "uniqueidentifier",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000001"));
        }
    }
}
