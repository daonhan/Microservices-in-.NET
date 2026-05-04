using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerce.Shared.Infrastructure.Outbox.Migrations
{
    /// <inheritdoc />
    public partial class OutboxFailureTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "OutboxEvents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "CorrelationId",
                table: "OutboxEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAttemptAt",
                table: "OutboxEvents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "OutboxEvents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "OutboxEvents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEvents_Sent_Status",
                table: "OutboxEvents",
                columns: new[] { "Sent", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxEvents_Sent_Status",
                table: "OutboxEvents");

            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "OutboxEvents");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "OutboxEvents");

            migrationBuilder.DropColumn(
                name: "LastAttemptAt",
                table: "OutboxEvents");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "OutboxEvents");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "OutboxEvents");
        }
    }
}
