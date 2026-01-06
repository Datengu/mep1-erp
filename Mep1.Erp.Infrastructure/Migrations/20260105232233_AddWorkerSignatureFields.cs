using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mep1.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerSignatureFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SignatureCapturedAtUtc",
                table: "Workers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatureName",
                table: "Workers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignatureCapturedAtUtc",
                table: "Workers");

            migrationBuilder.DropColumn(
                name: "SignatureName",
                table: "Workers");
        }
    }
}
