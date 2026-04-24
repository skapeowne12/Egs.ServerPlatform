using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Egs.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServerStartedUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartedUtc",
                table: "Servers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StartedUtc",
                table: "Servers");
        }
    }
}
