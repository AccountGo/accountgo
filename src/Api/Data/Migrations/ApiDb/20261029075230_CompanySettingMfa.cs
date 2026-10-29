// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <summary>
    /// MFA policy columns on CompanySetting. Mirrors D1 0026_security_schema.sql and SqliteSchemaUpgrader.
    /// Closes EF model drift so <c>Database.Migrate()</c> / has-pending-model-changes stay clean.
    /// </summary>
    public partial class CompanySettingMfa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "MfaGracePeriodEndsAt",
                table: "CompanySetting",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MfaPolicy",
                table: "CompanySetting",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RequireMfaEveryLogin",
                table: "CompanySetting",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MfaGracePeriodEndsAt",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "MfaPolicy",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "RequireMfaEveryLogin",
                table: "CompanySetting");
        }
    }
}
