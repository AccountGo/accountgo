// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    [DbContext(typeof(ApiDbContext))]
    [Migration("20261027000000_SecurityPolicy")]
    public class SecurityPolicy : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MfaPolicy",
                table: "CompanySetting",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "MfaGracePeriodEndsAt",
                table: "CompanySetting",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequireMfaEveryLogin",
                table: "CompanySetting",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequireMfaEveryLogin",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "MfaGracePeriodEndsAt",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "MfaPolicy",
                table: "CompanySetting");
        }
    }
}
