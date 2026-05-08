using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Product.Service.Migrations
{
    /// <inheritdoc />
    public partial class _20260507_SeedQaPhase3c_Product : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Products",
                columns: new[] { "Id", "Description", "Name", "Price", "ProductTypeId" },
                values: new object[,]
                {
                    { 9004, "QA inventory admin-ops product (threshold tripped)", "product-low-stock", 10.00m, 1 },
                    { 9005, "QA inventory admin-ops product (restock target)", "product-restock-target", 10.00m, 1 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 9004);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 9005);
        }
    }
}
