using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Egs.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServerSettingsJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SettingsJson",
                table: "Servers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SettingsJson",
                table: "Servers");
        }
    }
}
