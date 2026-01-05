using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mep1.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTimesheetEntryAuditAndSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "TimesheetEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "TimesheetEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeletedByWorkerId",
                table: "TimesheetEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "TimesheetEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "TimesheetEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdatedByWorkerId",
                table: "TimesheetEntries",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "TimesheetEntries");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "TimesheetEntries");

            migrationBuilder.DropColumn(
                name: "DeletedByWorkerId",
                table: "TimesheetEntries");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "TimesheetEntries");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "TimesheetEntries");

            migrationBuilder.DropColumn(
                name: "UpdatedByWorkerId",
                table: "TimesheetEntries");
        }
    }
}
