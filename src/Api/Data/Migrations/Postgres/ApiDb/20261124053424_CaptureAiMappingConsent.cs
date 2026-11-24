using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Api.Data.Migrations.Postgres.ApiDb
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
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReservedPages",
                table: "DocumentCaptureAiMappings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CaptureAiMappingConsents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    ConsentVersion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConsentedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ConsentedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RevokedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
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
