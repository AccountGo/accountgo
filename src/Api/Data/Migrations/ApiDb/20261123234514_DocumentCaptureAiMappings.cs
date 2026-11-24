using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class DocumentCaptureAiMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentCaptureAiMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    CaptureId = table.Column<int>(type: "int", nullable: false),
                    ExtractionGeneration = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LeaseUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RulesVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    MapperName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MapperVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LatencyMs = table.Column<int>(type: "int", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RuleHintsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 16384, nullable: true),
                    MappedFieldsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 65536, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentCaptureAiMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentCaptureAiMappings_DocumentCaptures_CompanyId_CaptureId",
                        columns: x => new { x.CompanyId, x.CaptureId },
                        principalTable: "DocumentCaptures",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCaptureAiMappings_Cache",
                table: "DocumentCaptureAiMappings",
                columns: new[] { "CompanyId", "ContentHash", "Kind", "RulesVersion", "MapperVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCaptureAiMappings_CompanyId_CaptureId_ExtractionGeneration",
                table: "DocumentCaptureAiMappings",
                columns: new[] { "CompanyId", "CaptureId", "ExtractionGeneration" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCaptureAiMappings_Status_LeaseUntil",
                table: "DocumentCaptureAiMappings",
                columns: new[] { "Status", "LeaseUntil" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentCaptureAiMappings");
        }
    }
}
