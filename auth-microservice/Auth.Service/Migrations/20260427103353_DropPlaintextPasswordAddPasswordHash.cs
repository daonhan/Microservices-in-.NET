using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auth.Service.Migrations
{
    /// <inheritdoc />
    public partial class DropPlaintextPasswordAddPasswordHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Password",
                table: "Users");

            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("d854813c-4a72-4afd-b431-878cba3ecf2a"),
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEDgcVTWsoKHvpybMHFtFOBxG0zYOvKUkB+xDTlq54OejnLzLBpFVNL0oIbrhJs7+hw==");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PasswordHash",
                table: "Users");

            migrationBuilder.AddColumn<string>(
                name: "Password",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("d854813c-4a72-4afd-b431-878cba3ecf2a"),
                column: "Password",
                value: "oKNrqkO7iC#G");
        }
    }
}
