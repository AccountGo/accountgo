// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    [DbContext(typeof(ApiDbContext))]
    [Migration("20261030140000_BankStatementLines")]
    public partial class BankStatementLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankStatementImport",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    BankId = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ImportedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ColumnMapJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementImport", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementImport_Bank_BankId",
                        column: x => x.BankId,
                        principalTable: "Bank",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    BankId = table.Column<int>(type: "int", nullable: false),
                    BankStatementImportId = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PostedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Payee = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Memo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MatchedGeneralLedgerLineId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementLine_Bank_BankId",
                        column: x => x.BankId,
                        principalTable: "Bank",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankStatementLine_BankStatementImport_BankStatementImportId",
                        column: x => x.BankStatementImportId,
                        principalTable: "BankStatementImport",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankStatementLine_GeneralLedgerLine_MatchedGeneralLedgerLineId",
                        column: x => x.MatchedGeneralLedgerLineId,
                        principalTable: "GeneralLedgerLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImport_BankId",
                table: "BankStatementImport",
                column: "BankId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImport_Company_Bank",
                table: "BankStatementImport",
                columns: new[] { "CompanyId", "BankId" });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImport_CompanyId",
                table: "BankStatementImport",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLine_BankId",
                table: "BankStatementLine",
                column: "BankId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLine_BankStatementImportId",
                table: "BankStatementLine",
                column: "BankStatementImportId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLine_Company_Bank_PostedDate",
                table: "BankStatementLine",
                columns: new[] { "CompanyId", "BankId", "PostedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLine_CompanyId",
                table: "BankStatementLine",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLine_MatchedGeneralLedgerLineId",
                table: "BankStatementLine",
                column: "MatchedGeneralLedgerLineId");

            migrationBuilder.CreateIndex(
                name: "UX_BankStatementLine_Company_Bank_Source_External",
                table: "BankStatementLine",
                columns: new[] { "CompanyId", "BankId", "Source", "ExternalId" },
                unique: true,
                filter: "[ExternalId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "BankStatementLine");
            migrationBuilder.DropTable(name: "BankStatementImport");
        }
    }
}
