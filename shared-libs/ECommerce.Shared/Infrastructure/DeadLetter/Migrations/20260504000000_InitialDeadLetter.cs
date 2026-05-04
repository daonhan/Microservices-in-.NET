using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerce.Shared.Infrastructure.DeadLetter.Migrations
{
    /// <inheritdoc />
    public partial class InitialDeadLetter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dead_letter_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_type = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    routing_key = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    original_queue = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    service = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    failure_reason = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    stack_trace = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    failed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    replayed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    replayed_by = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    discarded_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    discarded_by = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    discard_reason = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    correlation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dead_letter_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dead_letter_messages_failed_at",
                table: "dead_letter_messages",
                column: "failed_at");

            migrationBuilder.CreateIndex(
                name: "IX_dead_letter_messages_status",
                table: "dead_letter_messages",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dead_letter_messages");
        }
    }
}
