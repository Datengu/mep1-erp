using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mep1.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSoftDeleteToProjectCcfRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "ProjectCcfRefs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeletedByWorkerId",
                table: "ProjectCcfRefs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "ProjectCcfRefs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "ProjectCcfRefs");

            migrationBuilder.DropColumn(
                name: "DeletedByWorkerId",
                table: "ProjectCcfRefs");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "ProjectCcfRefs");
        }
    }
}
