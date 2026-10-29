using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class BankReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BankReconciliationId",
                table: "GeneralLedgerLine",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BankReconciliation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    BankId = table.Column<int>(type: "int", nullable: false),
                    StatementEndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StatementEndingBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BeginningBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UndoneBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UndoneAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UndoReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankReconciliation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankReconciliation_Bank_BankId",
                        column: x => x.BankId,
                        principalTable: "Bank",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankReconciliationItem",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    BankReconciliationId = table.Column<int>(type: "int", nullable: false),
                    GeneralLedgerLineId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DrCr = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankReconciliationItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankReconciliationItem_BankReconciliation_BankReconciliationId",
                        column: x => x.BankReconciliationId,
                        principalTable: "BankReconciliation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BankReconciliationItem_GeneralLedgerLine_GeneralLedgerLineId",
                        column: x => x.GeneralLedgerLineId,
                        principalTable: "GeneralLedgerLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerLine_BankReconciliationId",
                table: "GeneralLedgerLine",
                column: "BankReconciliationId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerLine_Company_BankReconciliationId",
                table: "GeneralLedgerLine",
                columns: new[] { "CompanyId", "BankReconciliationId" });

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliation_BankId",
                table: "BankReconciliation",
                column: "BankId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliation_Company_Bank_EndDate",
                table: "BankReconciliation",
                columns: new[] { "CompanyId", "BankId", "StatementEndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliation_CompanyId",
                table: "BankReconciliation",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "UX_BankReconciliation_OneDraft",
                table: "BankReconciliation",
                columns: new[] { "CompanyId", "BankId" },
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationItem_BankReconciliationId",
                table: "BankReconciliationItem",
                column: "BankReconciliationId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationItem_CompanyId",
                table: "BankReconciliationItem",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationItem_GeneralLedgerLineId",
                table: "BankReconciliationItem",
                column: "GeneralLedgerLineId");

            migrationBuilder.CreateIndex(
                name: "UX_BankReconciliationItem_Recon_Line",
                table: "BankReconciliationItem",
                columns: new[] { "BankReconciliationId", "GeneralLedgerLineId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_GeneralLedgerLine_BankReconciliation_BankReconciliationId",
                table: "GeneralLedgerLine",
                column: "BankReconciliationId",
                principalTable: "BankReconciliation",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GeneralLedgerLine_BankReconciliation_BankReconciliationId",
                table: "GeneralLedgerLine");

            migrationBuilder.DropTable(
                name: "BankReconciliationItem");

            migrationBuilder.DropTable(
                name: "BankReconciliation");

            migrationBuilder.DropIndex(
                name: "IX_GeneralLedgerLine_BankReconciliationId",
                table: "GeneralLedgerLine");

            migrationBuilder.DropIndex(
                name: "IX_GeneralLedgerLine_Company_BankReconciliationId",
                table: "GeneralLedgerLine");

            migrationBuilder.DropColumn(
                name: "BankReconciliationId",
                table: "GeneralLedgerLine");
        }
    }
}
