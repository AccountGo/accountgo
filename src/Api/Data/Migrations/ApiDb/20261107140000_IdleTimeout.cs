// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Api.Data.Migrations.ApiDb;

[DbContext(typeof(ApiDbContext))]
[Migration("20261107140000_IdleTimeout")]
public sealed class IdleTimeoutMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "IdleTimeoutMinutes",
            table: "CompanySetting",
            type: "INTEGER",
            nullable: false,
            defaultValue: 60);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "IdleTimeoutMinutes", table: "CompanySetting");
    }
}
