using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Service.Migrations
{
    /// <inheritdoc />
    public partial class _20260507_SeedQaPhase2_Inventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "StockItems",
                columns: new[] { "ProductId", "LowStockThreshold", "TotalOnHand", "TotalReserved" },
                values: new object[,]
                {
                    { 9002, 0, 25, 0 },
                    { 9003, 0, 0, 0 }
                });

            migrationBuilder.InsertData(
                table: "StockLevels",
                columns: new[] { "Id", "OnHand", "ProductId", "Reserved", "WarehouseId" },
                values: new object[,]
                {
                    { 9002, 25, 9002, 0, 1 },
                    { 9003, 0, 9003, 0, 1 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "StockItems",
                keyColumn: "ProductId",
                keyValue: 9002);

            migrationBuilder.DeleteData(
                table: "StockItems",
                keyColumn: "ProductId",
                keyValue: 9003);

            migrationBuilder.DeleteData(
                table: "StockLevels",
                keyColumn: "Id",
                keyValue: 9002);

            migrationBuilder.DeleteData(
                table: "StockLevels",
                keyColumn: "Id",
                keyValue: 9003);
        }
    }
}
