using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Egs.Infrastructure.Data.Migrations
{
    public partial class AddServerInstallPath : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InstallPath",
                table: "Servers",
                type: "TEXT",
                maxLength: 500,
                nullable: false,
                defaultValue: "");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InstallPath",
                table: "Servers");
        }
    }
}
