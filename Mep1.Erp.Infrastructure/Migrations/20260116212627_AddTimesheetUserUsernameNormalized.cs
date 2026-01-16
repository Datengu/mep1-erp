using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mep1.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTimesheetUserUsernameNormalized : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UsernameNormalized",
                table: "TimesheetUsers",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
                UPDATE TimesheetUsers
                SET UsernameNormalized = lower(trim(Username))
                WHERE Username IS NOT NULL AND UsernameNormalized = '';
                ");

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetUsers_UsernameNormalized",
                table: "TimesheetUsers",
                column: "UsernameNormalized",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TimesheetUsers_UsernameNormalized",
                table: "TimesheetUsers");

            migrationBuilder.DropColumn(
                name: "UsernameNormalized",
                table: "TimesheetUsers");
        }
    }
}
