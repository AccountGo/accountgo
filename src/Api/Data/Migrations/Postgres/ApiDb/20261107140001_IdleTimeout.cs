// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Api.Data.Postgres;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Api.Data.Migrations.Postgres.ApiDb;

[DbContext(typeof(PostgresApiDbContext))]
[Migration("20261107140001_IdleTimeout")]
public sealed class IdleTimeoutMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE \"CompanySetting\" ADD COLUMN \"IdleTimeoutMinutes\" integer NOT NULL DEFAULT 60");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE \"CompanySetting\" DROP COLUMN IF EXISTS \"IdleTimeoutMinutes\"");
    }
}
