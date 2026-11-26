using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Api.Data.Migrations.Postgres.ApiDb
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PublicId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: ""),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    StreamStartedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastHeartbeatAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SelectedRevision = table.Column<long>(type: "bigint", nullable: false),
                    SelectedCount = table.Column<int>(type: "integer", nullable: false),
                    SelectedBytes = table.Column<long>(type: "bigint", nullable: false),
                    WrittenCount = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    InventoryJson = table.Column<string>(type: "text", nullable: false)
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
                filter: "\"Status\" = 'running'");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentExportRuns_PublicId",
                table: "DocumentExportRuns",
                column: "PublicId",
                unique: true,
                filter: "\"PublicId\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentExportRuns");
        }
    }
}
