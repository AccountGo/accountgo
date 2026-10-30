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
    [Migration("20261030170000_StatementDescriptionRules")]
    public partial class StatementDescriptionRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankStatementDescriptionRule",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    DescriptionKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    VendorId = table.Column<int>(type: "int", nullable: true),
                    ExpenseAccountId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementDescriptionRule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementDescriptionRule_Account_ExpenseAccountId",
                        column: x => x.ExpenseAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankStatementDescriptionRule_Vendor_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementDescriptionRule_CompanyId",
                table: "BankStatementDescriptionRule",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementDescriptionRule_ExpenseAccountId",
                table: "BankStatementDescriptionRule",
                column: "ExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementDescriptionRule_VendorId",
                table: "BankStatementDescriptionRule",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "UX_BankStatementDescriptionRule_Company_DescriptionKey",
                table: "BankStatementDescriptionRule",
                columns: new[] { "CompanyId", "DescriptionKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "BankStatementDescriptionRule");
        }
    }
}
