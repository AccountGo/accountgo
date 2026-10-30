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
    [Migration("20261030160000_StatementLineExclusion")]
    public partial class StatementLineExclusion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExcludedAt",
                table: "BankStatementLine",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExcludedBy",
                table: "BankStatementLine",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExclusionReason",
                table: "BankStatementLine",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExclusionNote",
                table: "BankStatementLine",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLine_Company_Bank_ExcludedAt",
                table: "BankStatementLine",
                columns: new[] { "CompanyId", "BankId", "ExcludedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BankStatementLine_Company_Bank_ExcludedAt",
                table: "BankStatementLine");

            migrationBuilder.DropColumn(name: "ExcludedAt", table: "BankStatementLine");
            migrationBuilder.DropColumn(name: "ExcludedBy", table: "BankStatementLine");
            migrationBuilder.DropColumn(name: "ExclusionReason", table: "BankStatementLine");
            migrationBuilder.DropColumn(name: "ExclusionNote", table: "BankStatementLine");
        }
    }
}
