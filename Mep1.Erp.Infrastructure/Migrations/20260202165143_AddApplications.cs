using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mep1.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApplicationId",
                table: "Invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Applications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectCode = table.Column<string>(type: "text", nullable: false),
                    ApplicationNumber = table.Column<int>(type: "integer", nullable: false),
                    DateApplied = table.Column<DateTime>(type: "date", nullable: false),
                    SubmittedNetAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    AgreedNetAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    DateAgreed = table.Column<DateTime>(type: "date", nullable: true),
                    ExternalReference = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    ProjectId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Applications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Applications_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ApplicationId",
                table: "Invoices",
                column: "ApplicationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Applications_ProjectCode",
                table: "Applications",
                column: "ProjectCode");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_ProjectCode_ApplicationNumber",
                table: "Applications",
                columns: new[] { "ProjectCode", "ApplicationNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Applications_ProjectId",
                table: "Applications",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Applications_ApplicationId",
                table: "Invoices",
                column: "ApplicationId",
                principalTable: "Applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Applications_ApplicationId",
                table: "Invoices");

            migrationBuilder.DropTable(
                name: "Applications");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_ApplicationId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ApplicationId",
                table: "Invoices");
        }
    }
}
