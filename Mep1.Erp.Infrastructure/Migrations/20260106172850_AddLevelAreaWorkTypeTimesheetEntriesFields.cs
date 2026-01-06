using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mep1.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLevelAreaWorkTypeTimesheetEntriesFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AreasJson",
                table: "TimesheetEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LevelsJson",
                table: "TimesheetEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WorkType",
                table: "TimesheetEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AreasJson",
                table: "TimesheetEntries");

            migrationBuilder.DropColumn(
                name: "LevelsJson",
                table: "TimesheetEntries");

            migrationBuilder.DropColumn(
                name: "WorkType",
                table: "TimesheetEntries");
        }
    }
}
