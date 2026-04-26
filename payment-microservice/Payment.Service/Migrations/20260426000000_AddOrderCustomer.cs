using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payment.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderCustomers",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderCustomers", x => x.OrderId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderCustomers");
        }
    }
}
