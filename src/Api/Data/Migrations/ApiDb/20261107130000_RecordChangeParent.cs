// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Api.Data.Migrations.ApiDb;

[DbContext(typeof(ApiDbContext))]
[Migration("20261107130000_RecordChangeParent")]
public sealed class RecordChangeParentMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ParentEntityType",
            table: "RecordChanges",
            type: "TEXT",
            maxLength: 120,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ParentEntityId",
            table: "RecordChanges",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_RecordChanges_Company_Parent",
            table: "RecordChanges",
            columns: new[] { "CompanyId", "ParentEntityType", "ParentEntityId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_RecordChanges_Company_Parent",
            table: "RecordChanges");

        migrationBuilder.DropColumn(
            name: "ParentEntityType",
            table: "RecordChanges");

        migrationBuilder.DropColumn(
            name: "ParentEntityId",
            table: "RecordChanges");
    }
}
