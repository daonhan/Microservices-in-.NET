using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Payment.Service.Migrations
{
    /// <inheritdoc />
    public partial class SeedQaPhase3a_Payment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "OrderCustomers",
                columns: new[] { "OrderId", "CustomerId", "ReceivedAt" },
                values: new object[,]
                {
                    { new Guid("a0000000-0000-0000-0000-000000000001"), "5ff2d67e-c6b5-4870-911f-79393ed416fd", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("a0000000-0000-0000-0000-000000000002"), "5ff2d67e-c6b5-4870-911f-79393ed416fd", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.InsertData(
                table: "Payments",
                columns: new[] { "PaymentId", "Amount", "CreatedAt", "Currency", "CustomerId", "OrderId", "ProviderReference", "Status", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("b0000000-0000-0000-0000-000000000001"), 20.00m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "USD", "5ff2d67e-c6b5-4870-911f-79393ed416fd", new Guid("a0000000-0000-0000-0000-000000000001"), "INMEM-qa-happy-authorized", 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("b0000000-0000-0000-0000-000000000002"), 20.00m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "USD", "5ff2d67e-c6b5-4870-911f-79393ed416fd", new Guid("a0000000-0000-0000-0000-000000000002"), "INMEM-qa-happy-captured", 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "OrderCustomers",
                keyColumn: "OrderId",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "OrderCustomers",
                keyColumn: "OrderId",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "Payments",
                keyColumn: "PaymentId",
                keyValue: new Guid("b0000000-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "Payments",
                keyColumn: "PaymentId",
                keyValue: new Guid("b0000000-0000-0000-0000-000000000002"));
        }
    }
}
