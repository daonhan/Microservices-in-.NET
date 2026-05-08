using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Service.Migrations
{
    /// <inheritdoc />
    public partial class _20260507_SeedQaPhase3c_Inventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "StockItems",
                columns: new[] { "ProductId", "LowStockThreshold", "TotalOnHand", "TotalReserved" },
                values: new object[,]
                {
                    { 9004, 2, 1, 0 },
                    { 9005, 0, 0, 0 }
                });

            migrationBuilder.InsertData(
                table: "StockLevels",
                columns: new[] { "Id", "OnHand", "ProductId", "Reserved", "WarehouseId" },
                values: new object[,]
                {
                    { 9004, 1, 9004, 0, 1 },
                    { 9005, 0, 9005, 0, 1 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "StockItems",
                keyColumn: "ProductId",
                keyValue: 9004);

            migrationBuilder.DeleteData(
                table: "StockItems",
                keyColumn: "ProductId",
                keyValue: 9005);

            migrationBuilder.DeleteData(
                table: "StockLevels",
                keyColumn: "Id",
                keyValue: 9004);

            migrationBuilder.DeleteData(
                table: "StockLevels",
                keyColumn: "Id",
                keyValue: 9005);
        }
    }
}
