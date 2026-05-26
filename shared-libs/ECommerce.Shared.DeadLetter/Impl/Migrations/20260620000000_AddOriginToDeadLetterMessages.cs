using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerce.Shared.Infrastructure.DeadLetter.Migrations
{
    /// <inheritdoc />
    public partial class AddOriginToDeadLetterMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "origin",
                table: "dead_letter_messages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_dead_letter_messages_origin",
                table: "dead_letter_messages",
                column: "origin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_dead_letter_messages_origin",
                table: "dead_letter_messages");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "dead_letter_messages");
        }
    }
}
