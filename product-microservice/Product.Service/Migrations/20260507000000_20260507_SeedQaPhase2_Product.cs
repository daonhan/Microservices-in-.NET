using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Product.Service.Migrations
{
    /// <inheritdoc />
    public partial class _20260507_SeedQaPhase2_Product : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Products",
                columns: new[] { "Id", "Description", "Name", "Price", "ProductTypeId" },
                values: new object[,]
                {
                    { 9002, "QA payment-decline product (cents == 99)", "product-decline", 9.99m, 1 },
                    { 9003, "QA stock-shortage product (zero on hand)", "product-zero-stock", 10.00m, 1 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 9002);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 9003);
        }
    }
}
