using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.Postgres.ApiDb
{
    /// <inheritdoc />
    public partial class SourceReferencedJournals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceKey",
                table: "JournalEntryHeader",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceSystem",
                table: "JournalEntryHeader",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryHeader_CompanyId_SourceSystem_SourceKey",
                table: "JournalEntryHeader",
                columns: new[] { "CompanyId", "SourceSystem", "SourceKey" },
                unique: true,
                filter: "\"SourceSystem\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JournalEntryHeader_CompanyId_SourceSystem_SourceKey",
                table: "JournalEntryHeader");

            migrationBuilder.DropColumn(
                name: "SourceKey",
                table: "JournalEntryHeader");

            migrationBuilder.DropColumn(
                name: "SourceSystem",
                table: "JournalEntryHeader");
        }
    }
}
