// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Api.Data.Migrations.IdentityDb;

[DbContext(typeof(ApplicationIdentityDbContext))]
[Migration("20261107160000_DeleteOrphanExternalLogins")]
public sealed class DeleteOrphanExternalLogins : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DELETE FROM AspNetUserLogins WHERE UserId IN (SELECT Id FROM AspNetUsers WHERE DeletedAt IS NOT NULL);");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
