// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Api.Data.Postgres;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Api.Data.Migrations.Postgres.ApiDb;

[DbContext(typeof(PostgresApiDbContext))]
[Migration("20261102040000_StatementDraftBills")]
public sealed class StatementDraftBills : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<int>("DraftBillId", "BankStatementLine", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn("DraftBillId", "BankStatementLine");
}
