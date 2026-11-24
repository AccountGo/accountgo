using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class CaptureAiMappingConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TextPageCount",
                table: "DocumentCaptures",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReservedPages",
                table: "DocumentCaptureAiMappings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CaptureAiMappingConsents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    ConsentVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ConsentedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsentedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaptureAiMappingConsents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaptureAiMappingConsents_CompanyId",
                table: "CaptureAiMappingConsents",
                column: "CompanyId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaptureAiMappingConsents");

            migrationBuilder.DropColumn(
                name: "TextPageCount",
                table: "DocumentCaptures");

            migrationBuilder.DropColumn(
                name: "ReservedPages",
                table: "DocumentCaptureAiMappings");
        }
    }
}
