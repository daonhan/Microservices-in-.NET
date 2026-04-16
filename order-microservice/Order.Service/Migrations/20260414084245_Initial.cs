using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Order.Service.Migrations;

/// <inheritdoc />
public partial class Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Orders",
            columns: table => new
            {
                OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CustomerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                OrderDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Orders", x => x.OrderId);
            });

        migrationBuilder.CreateTable(
            name: "OrderProducts",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ProductId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Quantity = table.Column<int>(type: "int", nullable: false),
                OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrderProducts", x => x.Id);
                table.ForeignKey(
                    name: "FK_OrderProducts_Orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "Orders",
                    principalColumn: "OrderId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_OrderProducts_OrderId",
            table: "OrderProducts",
            column: "OrderId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "OrderProducts");

        migrationBuilder.DropTable(
            name: "Orders");
    }
}
