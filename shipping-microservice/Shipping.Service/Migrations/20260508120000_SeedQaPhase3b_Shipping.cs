using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Shipping.Service.Migrations
{
    /// <inheritdoc />
    public partial class SeedQaPhase3b_Shipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Shipments",
                columns: new[] { "Id", "CarrierKey", "CreatedAt", "CustomerId", "LabelRef", "OrderId", "QuotedPriceAmount", "QuotedPriceCurrency", "Status", "TrackingNumber", "WarehouseId" },
                values: new object[,]
                {
                    { new Guid("c0000000-0000-0000-0000-000000000001"), null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "5ff2d67e-c6b5-4870-911f-79393ed416fd", null, new Guid("d0000000-0000-0000-0000-000000000001"), null, null, 0, null, 1 },
                    { new Guid("c0000000-0000-0000-0000-000000000002"), null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "5ff2d67e-c6b5-4870-911f-79393ed416fd", null, new Guid("d0000000-0000-0000-0000-000000000002"), null, null, 1, null, 1 },
                    { new Guid("c0000000-0000-0000-0000-000000000003"), null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "5ff2d67e-c6b5-4870-911f-79393ed416fd", null, new Guid("d0000000-0000-0000-0000-000000000003"), null, null, 2, null, 1 },
                    { new Guid("c0000000-0000-0000-0000-000000000004"), "fake-ground", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "5ff2d67e-c6b5-4870-911f-79393ed416fd", "label://qa/QA-TRACK-DISPATCHED-001", new Guid("d0000000-0000-0000-0000-000000000004"), 5.00m, "USD", 3, "QA-TRACK-DISPATCHED-001", 1 },
                    { new Guid("c0000000-0000-0000-0000-000000000005"), null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "5ff2d67e-c6b5-4870-911f-79393ed416fd", null, new Guid("d0000000-0000-0000-0000-000000000005"), null, null, 0, null, 1 }
                });

            migrationBuilder.InsertData(
                table: "ShipmentLines",
                columns: new[] { "Id", "ProductId", "Quantity", "ShipmentId" },
                values: new object[,]
                {
                    { 90001, 9001, 2, new Guid("c0000000-0000-0000-0000-000000000001") },
                    { 90002, 9001, 2, new Guid("c0000000-0000-0000-0000-000000000002") },
                    { 90003, 9001, 2, new Guid("c0000000-0000-0000-0000-000000000003") },
                    { 90004, 9001, 2, new Guid("c0000000-0000-0000-0000-000000000004") },
                    { 90005, 9001, 2, new Guid("c0000000-0000-0000-0000-000000000005") }
                });

            migrationBuilder.InsertData(
                table: "ShipmentStatusHistory",
                columns: new[] { "Id", "OccurredAt", "Reason", "ShipmentId", "Source", "Status" },
                values: new object[,]
                {
                    { 90001, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, new Guid("c0000000-0000-0000-0000-000000000001"), 0, 0 },
                    { 90002, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, new Guid("c0000000-0000-0000-0000-000000000002"), 0, 1 },
                    { 90003, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, new Guid("c0000000-0000-0000-0000-000000000003"), 0, 2 },
                    { 90004, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, new Guid("c0000000-0000-0000-0000-000000000004"), 0, 3 },
                    { 90005, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, new Guid("c0000000-0000-0000-0000-000000000005"), 0, 0 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ShipmentLines",
                keyColumn: "Id",
                keyValues: new object[] { 90001, 90002, 90003, 90004, 90005 });

            migrationBuilder.DeleteData(
                table: "ShipmentStatusHistory",
                keyColumn: "Id",
                keyValues: new object[] { 90001, 90002, 90003, 90004, 90005 });

            migrationBuilder.DeleteData(
                table: "Shipments",
                keyColumn: "Id",
                keyValues: new object[]
                {
                    new Guid("c0000000-0000-0000-0000-000000000001"),
                    new Guid("c0000000-0000-0000-0000-000000000002"),
                    new Guid("c0000000-0000-0000-0000-000000000003"),
                    new Guid("c0000000-0000-0000-0000-000000000004"),
                    new Guid("c0000000-0000-0000-0000-000000000005")
                });
        }
    }
}
