using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Saga.Service.Infrastructure.Data.EntityFramework;

#nullable disable

namespace Saga.Service.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(SagaContext))]
    [Migration("20260517150000_AddSagaLastCommandId")]
    public partial class AddSagaLastCommandId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LastCommandId",
                table: "SagaInstances",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastCommandId",
                table: "SagaInstances");
        }
    }
}
