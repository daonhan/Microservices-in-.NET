using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Service.Migrations
{
    /// <inheritdoc />
    public partial class Phase5Sync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op: only the model's nullability annotation for the
            // SQL Server `rowversion` columns changed. SQL Server does not
            // allow ALTER COLUMN on a timestamp/rowversion column, and the
            // physical schema already matches the desired model. This
            // migration exists solely to keep the model snapshot in sync
            // and clear the EF Core PendingModelChangesWarning.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op (see Up).
        }
    }
}
