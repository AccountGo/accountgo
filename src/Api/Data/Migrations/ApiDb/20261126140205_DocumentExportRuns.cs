using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class DocumentExportRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentExportRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    PublicId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: ""),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StreamStartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SelectedRevision = table.Column<long>(type: "bigint", nullable: false),
                    SelectedCount = table.Column<int>(type: "int", nullable: false),
                    SelectedBytes = table.Column<long>(type: "bigint", nullable: false),
                    WrittenCount = table.Column<int>(type: "int", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    InventoryJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentExportRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentExportRuns_Company_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Company",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentExportRuns_CompanyId_CreatedAt",
                table: "DocumentExportRuns",
                columns: new[] { "CompanyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentExportRuns_CompanyId_PublicId",
                table: "DocumentExportRuns",
                columns: new[] { "CompanyId", "PublicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentExportRuns_OneRunning",
                table: "DocumentExportRuns",
                column: "CompanyId",
                unique: true,
                filter: "[Status] = 'running'");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentExportRuns_PublicId",
                table: "DocumentExportRuns",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentExportRuns");
        }
    }
}
