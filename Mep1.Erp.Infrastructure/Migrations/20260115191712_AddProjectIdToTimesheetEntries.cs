using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mep1.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectIdToTimesheetEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add ProjectId column
            migrationBuilder.AddColumn<int>(
                name: "ProjectId",
                table: "TimesheetEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Index for joins / lookups
            migrationBuilder.CreateIndex(
                name: "IX_TimesheetEntries_ProjectId",
                table: "TimesheetEntries",
                column: "ProjectId");

            // FK to Projects
            migrationBuilder.AddForeignKey(
                name: "FK_TimesheetEntries_Projects_ProjectId",
                table: "TimesheetEntries",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TimesheetEntries_Projects_ProjectId",
                table: "TimesheetEntries");

            migrationBuilder.DropIndex(
                name: "IX_TimesheetEntries_ProjectId",
                table: "TimesheetEntries");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "TimesheetEntries");
        }
    }
}
