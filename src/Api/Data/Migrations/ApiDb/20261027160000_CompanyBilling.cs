// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <summary>
    /// Provider billing columns on Company. Mirrors D1 0028_company_billing.sql and SqliteSchemaUpgrader.
    /// Hand-authored (same pattern as SecurityPolicy) so SQLite design-time does not rewrite SQL Server types.
    /// </summary>
    [DbContext(typeof(ApiDbContext))]
    [Migration("20261027160000_CompanyBilling")]
    public class CompanyBilling : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillingProvider",
                table: "Company",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingCustomerId",
                table: "Company",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingSubscriptionId",
                table: "Company",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CurrentPeriodEnd",
                table: "Company",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CancelAtPeriodEnd",
                table: "Company",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BillingManagedManually",
                table: "Company",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "Company",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "BillingProvider", "BillingCustomerId", "BillingSubscriptionId", "CurrentPeriodEnd", "CancelAtPeriodEnd", "BillingManagedManually" },
                values: new object[] { null, null, null, null, false, false });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "BillingManagedManually", table: "Company");
            migrationBuilder.DropColumn(name: "CancelAtPeriodEnd", table: "Company");
            migrationBuilder.DropColumn(name: "CurrentPeriodEnd", table: "Company");
            migrationBuilder.DropColumn(name: "BillingSubscriptionId", table: "Company");
            migrationBuilder.DropColumn(name: "BillingCustomerId", table: "Company");
            migrationBuilder.DropColumn(name: "BillingProvider", table: "Company");
        }
    }
}
