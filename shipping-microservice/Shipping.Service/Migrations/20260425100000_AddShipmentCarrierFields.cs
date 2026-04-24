using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shipping.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentCarrierFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CarrierKey",
                table: "Shipments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LabelRef",
                table: "Shipments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QuotedPriceAmount",
                table: "Shipments",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuotedPriceCurrency",
                table: "Shipments",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingAddress_City",
                table: "Shipments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingAddress_Country",
                table: "Shipments",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingAddress_Line1",
                table: "Shipments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingAddress_Line2",
                table: "Shipments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingAddress_PostalCode",
                table: "Shipments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingAddress_Recipient",
                table: "Shipments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingAddress_State",
                table: "Shipments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackingNumber",
                table: "Shipments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CarrierKey",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "LabelRef",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "QuotedPriceAmount",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "QuotedPriceCurrency",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ShippingAddress_City",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ShippingAddress_Country",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ShippingAddress_Line1",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ShippingAddress_Line2",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ShippingAddress_PostalCode",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ShippingAddress_Recipient",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ShippingAddress_State",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "TrackingNumber",
                table: "Shipments");
        }
    }
}
