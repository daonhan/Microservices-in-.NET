using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Service.Migrations
{
    /// <inheritdoc />
    public partial class _20260506_SeedQaData_Inventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "StockItems",
                columns: new[] { "ProductId", "LowStockThreshold", "TotalOnHand", "TotalReserved" },
                values: new object[] { 9001, 0, 25, 0 });

            migrationBuilder.InsertData(
                table: "StockLevels",
                columns: new[] { "Id", "OnHand", "ProductId", "Reserved", "WarehouseId" },
                values: new object[] { 9001, 25, 9001, 0, 1 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "StockItems",
                keyColumn: "ProductId",
                keyValue: 9001);

            migrationBuilder.DeleteData(
                table: "StockLevels",
                keyColumn: "Id",
                keyValue: 9001);
        }
    }
}
