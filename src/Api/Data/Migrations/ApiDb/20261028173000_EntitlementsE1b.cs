// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <summary>
    /// DisabledModulesJson + PaidAccessEndedAt. Mirrors D1 0030_entitlements_e1b.sql and SqliteSchemaUpgrader.
    /// Hand-authored so SQLite design-time does not rewrite SQL Server types. Backfill lives in D1/upgrader.
    /// </summary>
    [DbContext(typeof(ApiDbContext))]
    [Migration("20261028173000_EntitlementsE1b")]
    public class EntitlementsE1b : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisabledModulesJson",
                table: "Company",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAccessEndedAt",
                table: "Company",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DisabledModulesJson", table: "Company");
            migrationBuilder.DropColumn(name: "PaidAccessEndedAt", table: "Company");
        }
    }
}
