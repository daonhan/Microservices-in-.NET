using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Shipping.Service.Migrations
{
    /// <inheritdoc />
    public partial class SeedQaPhase3c_Shipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Shipments",
                columns: new[] { "Id", "CarrierKey", "CreatedAt", "CustomerId", "LabelRef", "OrderId", "QuotedPriceAmount", "QuotedPriceCurrency", "Status", "TrackingNumber", "WarehouseId" },
                values: new object[,]
                {
                    { new Guid("c0000000-0000-0000-0000-000000000006"), "fake-ground", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "5ff2d67e-c6b5-4870-911f-79393ed416fd", "label://qa/QA-TRACK-DISPATCHED-FAIL-001", new Guid("d0000000-0000-0000-0000-000000000006"), 5.00m, "USD", 3, "QA-TRACK-DISPATCHED-FAIL-001", 1 },
                    { new Guid("c0000000-0000-0000-0000-000000000007"), "fake-ground", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "5ff2d67e-c6b5-4870-911f-79393ed416fd", "label://qa/QA-TRACK-DISPATCHED-RETURN-001", new Guid("d0000000-0000-0000-0000-000000000007"), 5.00m, "USD", 3, "QA-TRACK-DISPATCHED-RETURN-001", 1 }
                });

            migrationBuilder.InsertData(
                table: "ShipmentLines",
                columns: new[] { "Id", "ProductId", "Quantity", "ShipmentId" },
                values: new object[,]
                {
                    { 90006, 9001, 2, new Guid("c0000000-0000-0000-0000-000000000006") },
                    { 90007, 9001, 2, new Guid("c0000000-0000-0000-0000-000000000007") }
                });

            migrationBuilder.InsertData(
                table: "ShipmentStatusHistory",
                columns: new[] { "Id", "OccurredAt", "Reason", "ShipmentId", "Source", "Status" },
                values: new object[,]
                {
                    { 90006, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, new Guid("c0000000-0000-0000-0000-000000000006"), 0, 3 },
                    { 90007, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, new Guid("c0000000-0000-0000-0000-000000000007"), 0, 3 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ShipmentLines",
                keyColumn: "Id",
                keyValue: 90006);

            migrationBuilder.DeleteData(
                table: "ShipmentLines",
                keyColumn: "Id",
                keyValue: 90007);

            migrationBuilder.DeleteData(
                table: "ShipmentStatusHistory",
                keyColumn: "Id",
                keyValue: 90006);

            migrationBuilder.DeleteData(
                table: "ShipmentStatusHistory",
                keyColumn: "Id",
                keyValue: 90007);

            migrationBuilder.DeleteData(
                table: "Shipments",
                keyColumn: "Id",
                keyValue: new Guid("c0000000-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                table: "Shipments",
                keyColumn: "Id",
                keyValue: new Guid("c0000000-0000-0000-0000-000000000007"));
        }
    }
}
