// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Api.Data.Schema;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Api.Data.Migrations.IdentityDb;

[DbContext(typeof(ApplicationIdentityDbContext))]
[Migration("20261107140000_SecurityAuditEventRetention")]
public sealed class SecurityAuditEventRetention : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SqliteSecurityAuditEventsSql.UpdateTrigger);
        migrationBuilder.Sql(SqliteSecurityAuditEventsSql.DeleteTrigger);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SqliteSecurityAuditEventsSql.DropDeleteTrigger);
        migrationBuilder.Sql(SqliteSecurityAuditEventsSql.DropUpdateTrigger);
    }
}
