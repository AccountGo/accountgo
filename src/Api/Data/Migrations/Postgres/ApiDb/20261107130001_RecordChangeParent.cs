// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Api.Data.Postgres;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Api.Data.Migrations.Postgres.ApiDb;

[DbContext(typeof(PostgresApiDbContext))]
[Migration("20261107130001_RecordChangeParent")]
public sealed class RecordChangeParentMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE \"RecordChanges\" ADD COLUMN \"ParentEntityType\" character varying(120) NULL");
        migrationBuilder.Sql("ALTER TABLE \"RecordChanges\" ADD COLUMN \"ParentEntityId\" integer NULL");
        migrationBuilder.Sql("CREATE INDEX \"IX_RecordChanges_Company_Parent\" ON \"RecordChanges\" (\"CompanyId\", \"ParentEntityType\", \"ParentEntityId\")");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_RecordChanges_Company_Parent\"");
        migrationBuilder.Sql("ALTER TABLE \"RecordChanges\" DROP COLUMN IF EXISTS \"ParentEntityId\"");
        migrationBuilder.Sql("ALTER TABLE \"RecordChanges\" DROP COLUMN IF EXISTS \"ParentEntityType\"");
    }
}
