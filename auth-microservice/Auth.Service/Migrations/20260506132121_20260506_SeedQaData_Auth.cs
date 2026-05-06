using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Auth.Service.Migrations
{
    /// <inheritdoc />
    public partial class _20260506_SeedQaData_Auth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "PasswordHash", "Role", "Username" },
                values: new object[,]
                {
                    { new Guid("00faac97-9ae4-4b7f-b8aa-00e7c569dd66"), "AQAAAAIAAYagAAAAEDgcVTWsoKHvpybMHFtFOBxG0zYOvKUkB+xDTlq54OejnLzLBpFVNL0oIbrhJs7+hw==", "Customer", "customer-cancel@qa.test" },
                    { new Guid("5ff2d67e-c6b5-4870-911f-79393ed416fd"), "AQAAAAIAAYagAAAAEDgcVTWsoKHvpybMHFtFOBxG0zYOvKUkB+xDTlq54OejnLzLBpFVNL0oIbrhJs7+hw==", "Customer", "customer-happy@qa.test" },
                    { new Guid("be0d0a1d-c8fe-4b17-bf6a-051e8c809aa6"), "AQAAAAIAAYagAAAAEDgcVTWsoKHvpybMHFtFOBxG0zYOvKUkB+xDTlq54OejnLzLBpFVNL0oIbrhJs7+hw==", "Customer", "customer-decline@qa.test" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00faac97-9ae4-4b7f-b8aa-00e7c569dd66"));

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("5ff2d67e-c6b5-4870-911f-79393ed416fd"));

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("be0d0a1d-c8fe-4b17-bf6a-051e8c809aa6"));
        }
    }
}
