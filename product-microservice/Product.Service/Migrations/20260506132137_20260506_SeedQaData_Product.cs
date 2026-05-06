using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Product.Service.Migrations
{
    /// <inheritdoc />
    public partial class _20260506_SeedQaData_Product : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Products",
                columns: new[] { "Id", "Description", "Name", "Price", "ProductTypeId" },
                values: new object[] { 9001, "QA happy-path product", "product-happy", 10.00m, 1 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 9001);
        }
    }
}
