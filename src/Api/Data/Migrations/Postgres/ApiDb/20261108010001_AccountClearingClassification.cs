// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
using Api.Data.Postgres;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Api.Data.Migrations.Postgres.ApiDb;
[DbContext(typeof(PostgresApiDbContext))]
[Migration("20261108010001_AccountClearingClassification")]
public sealed class AccountClearingClassification : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<bool>(name: "IsSuspenseOrClearing", table: "Account", type: "boolean", nullable: false, defaultValue: false);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(name: "IsSuspenseOrClearing", table: "Account");
}
