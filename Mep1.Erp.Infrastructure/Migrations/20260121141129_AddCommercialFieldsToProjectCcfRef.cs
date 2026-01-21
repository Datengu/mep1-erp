using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mep1.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCommercialFieldsToProjectCcfRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ActualValue",
                table: "ProjectCcfRefs",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AgreedDateUtc",
                table: "ProjectCcfRefs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AgreedValue",
                table: "ProjectCcfRefs",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedValue",
                table: "ProjectCcfRefs",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastValueUpdatedUtc",
                table: "ProjectCcfRefs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "ProjectCcfRefs",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuotedDateUtc",
                table: "ProjectCcfRefs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QuotedValue",
                table: "ProjectCcfRefs",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "ProjectCcfRefs",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActualValue",
                table: "ProjectCcfRefs");

            migrationBuilder.DropColumn(
                name: "AgreedDateUtc",
                table: "ProjectCcfRefs");

            migrationBuilder.DropColumn(
                name: "AgreedValue",
                table: "ProjectCcfRefs");

            migrationBuilder.DropColumn(
                name: "EstimatedValue",
                table: "ProjectCcfRefs");

            migrationBuilder.DropColumn(
                name: "LastValueUpdatedUtc",
                table: "ProjectCcfRefs");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "ProjectCcfRefs");

            migrationBuilder.DropColumn(
                name: "QuotedDateUtc",
                table: "ProjectCcfRefs");

            migrationBuilder.DropColumn(
                name: "QuotedValue",
                table: "ProjectCcfRefs");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "ProjectCcfRefs");
        }
    }
}
