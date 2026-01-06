using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mep1.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMustChangePasswordToTimesheetUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "TimesheetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordChangedAtUtc",
                table: "TimesheetUsers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "TimesheetUsers");

            migrationBuilder.DropColumn(
                name: "PasswordChangedAtUtc",
                table: "TimesheetUsers");
        }
    }
}
