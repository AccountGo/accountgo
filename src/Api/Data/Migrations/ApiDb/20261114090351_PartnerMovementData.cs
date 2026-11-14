using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class PartnerMovementData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PartnerSetupJournalEntryId",
                table: "CompanySetting",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartnerId",
                table: "BankStatementDescriptionRule",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementDescriptionRule_PartnerId",
                table: "BankStatementDescriptionRule",
                column: "PartnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_BankStatementDescriptionRule_Partners_PartnerId",
                table: "BankStatementDescriptionRule",
                column: "PartnerId",
                principalTable: "Partners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BankStatementDescriptionRule_Partners_PartnerId",
                table: "BankStatementDescriptionRule");

            migrationBuilder.DropIndex(
                name: "IX_BankStatementDescriptionRule_PartnerId",
                table: "BankStatementDescriptionRule");

            migrationBuilder.DropColumn(
                name: "PartnerSetupJournalEntryId",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "PartnerId",
                table: "BankStatementDescriptionRule");

        }
    }
}
