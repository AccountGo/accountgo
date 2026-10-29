// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <summary>
    /// Bank account columns, unique indexes, and D11 AccountId rules for #413 Phase 1A.
    /// Mirrors D1 0032_bank_accounts.sql and SqliteSchemaUpgrader / BankAccountCleanup.
    /// Hand-authored so SQLite design-time does not rewrite SQL Server types.
    /// Clean-up of invalid/shared AccountId values is in BankAccountCleanup (SQLite) and
    /// scripts/d1/bank-preflight.sql (D1); SQL Server installs are expected to be clean or empty.
    /// </summary>
    [DbContext(typeof(ApiDbContext))]
    [Migration("20261029120000_BankAccounts")]
    public class BankAccounts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keep IX_Bank_AccountId for the Account FK; add unique composites alongside it.
            // Name stays nullable until blank names are filled and defaults collapsed.

            migrationBuilder.AddColumn<string>(
                name: "RoutingNumber",
                table: "Bank",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Iban",
                table: "Bank",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Swift",
                table: "Bank",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefaultForReceipts",
                table: "Bank",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefaultForPayments",
                table: "Bank",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningBalance",
                table: "Bank",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OpeningBalanceDate",
                table: "Bank",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OpeningBalanceGeneralLedgerHeaderId",
                table: "Bank",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Bank",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "Bank",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.Sql(@"
-- One receipts default per company (lowest Id keeps IsDefault / IsDefaultForReceipts).
UPDATE b SET IsDefaultForReceipts = 1
FROM Bank AS b
WHERE b.IsDefault = 1
  AND b.IsDefaultForReceipts = 0
  AND NOT EXISTS (
      SELECT 1 FROM Bank AS other
      WHERE other.CompanyId = b.CompanyId AND other.IsDefaultForReceipts = 1);

UPDATE Bank SET ArchivedAt = SYSUTCDATETIME() WHERE IsActive = 0 AND ArchivedAt IS NULL;

UPDATE b SET Name = a.AccountName
FROM Bank AS b
INNER JOIN Account AS a ON a.Id = b.AccountId AND a.CompanyId = b.CompanyId
WHERE (b.Name IS NULL OR LTRIM(RTRIM(b.Name)) = '')
  AND a.AccountName IS NOT NULL
  AND LTRIM(RTRIM(a.AccountName)) <> '';

UPDATE Bank
SET Name = 'Bank ' + CAST(Id AS nvarchar(20))
WHERE Name IS NULL OR LTRIM(RTRIM(Name)) = '';

UPDATE b
SET Name = b.Name + ' (' + CAST(b.Id AS nvarchar(20)) + ')'
FROM Bank AS b
WHERE EXISTS (
    SELECT 1 FROM Bank AS other
    WHERE other.CompanyId = b.CompanyId
      AND LOWER(other.Name) = LOWER(b.Name)
      AND other.Id < b.Id);

UPDATE Bank SET IsDefaultForReceipts = 0
WHERE IsDefaultForReceipts = 1
  AND Id NOT IN (
      SELECT MinId FROM (
          SELECT MIN(Id) AS MinId FROM Bank WHERE IsDefaultForReceipts = 1 GROUP BY CompanyId
      ) AS keepers);

UPDATE Bank SET IsDefaultForPayments = 0
WHERE IsDefaultForPayments = 1
  AND Id NOT IN (
      SELECT MinId FROM (
          SELECT MIN(Id) AS MinId FROM Bank WHERE IsDefaultForPayments = 1 GROUP BY CompanyId
      ) AS keepers);
");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Bank",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_Bank_Company_Account",
                table: "Bank",
                columns: new[] { "CompanyId", "AccountId" },
                unique: true,
                filter: "[AccountId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Bank_Company_Name",
                table: "Bank",
                columns: new[] { "CompanyId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Bank_DefaultReceipts",
                table: "Bank",
                column: "CompanyId",
                unique: true,
                filter: "[IsDefaultForReceipts] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_Bank_DefaultPayments",
                table: "Bank",
                column: "CompanyId",
                unique: true,
                filter: "[IsDefaultForPayments] = 1");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "UX_Bank_DefaultPayments", table: "Bank");
            migrationBuilder.DropIndex(name: "UX_Bank_DefaultReceipts", table: "Bank");
            migrationBuilder.DropIndex(name: "UX_Bank_Company_Name", table: "Bank");
            migrationBuilder.DropIndex(name: "UX_Bank_Company_Account", table: "Bank");

            migrationBuilder.DropColumn(name: "CurrencyCode", table: "Bank");
            migrationBuilder.DropColumn(name: "ArchivedAt", table: "Bank");
            migrationBuilder.DropColumn(name: "OpeningBalanceGeneralLedgerHeaderId", table: "Bank");
            migrationBuilder.DropColumn(name: "OpeningBalanceDate", table: "Bank");
            migrationBuilder.DropColumn(name: "OpeningBalance", table: "Bank");
            migrationBuilder.DropColumn(name: "IsDefaultForPayments", table: "Bank");
            migrationBuilder.DropColumn(name: "IsDefaultForReceipts", table: "Bank");
            migrationBuilder.DropColumn(name: "Swift", table: "Bank");
            migrationBuilder.DropColumn(name: "Iban", table: "Bank");
            migrationBuilder.DropColumn(name: "RoutingNumber", table: "Bank");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Bank",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);
        }
    }
}
