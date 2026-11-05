// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Api.Data.Postgres;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Api.Data.Migrations.Postgres.ApiDb;

[DbContext(typeof(PostgresApiDbContext))]
[Migration("20261105120000_CompanyActivationState")]
public sealed class CompanyActivationState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ActivationState",
            table: "Company",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "active");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "ActivationState", table: "Company");
}
