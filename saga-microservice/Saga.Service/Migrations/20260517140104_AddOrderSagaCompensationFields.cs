using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saga.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderSagaCompensationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "OrderSagaStates",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompensationOrigin",
                table: "OrderSagaStates",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Amount",
                table: "OrderSagaStates");

            migrationBuilder.DropColumn(
                name: "CompensationOrigin",
                table: "OrderSagaStates");
        }
    }
}
