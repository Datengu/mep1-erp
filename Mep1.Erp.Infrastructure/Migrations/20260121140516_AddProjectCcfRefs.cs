using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mep1.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectCcfRefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CcfRef",
                table: "TimesheetEntries");

            migrationBuilder.AddColumn<int>(
                name: "ProjectCcfRefId",
                table: "TimesheetEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectCcfRefs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectCcfRefs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectCcfRefs_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetEntries_ProjectCcfRefId",
                table: "TimesheetEntries",
                column: "ProjectCcfRefId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCcfRefs_ProjectId_Code",
                table: "ProjectCcfRefs",
                columns: new[] { "ProjectId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TimesheetEntries_ProjectCcfRefs_ProjectCcfRefId",
                table: "TimesheetEntries",
                column: "ProjectCcfRefId",
                principalTable: "ProjectCcfRefs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TimesheetEntries_ProjectCcfRefs_ProjectCcfRefId",
                table: "TimesheetEntries");

            migrationBuilder.DropTable(
                name: "ProjectCcfRefs");

            migrationBuilder.DropIndex(
                name: "IX_TimesheetEntries_ProjectCcfRefId",
                table: "TimesheetEntries");

            migrationBuilder.DropColumn(
                name: "ProjectCcfRefId",
                table: "TimesheetEntries");

            migrationBuilder.AddColumn<string>(
                name: "CcfRef",
                table: "TimesheetEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
