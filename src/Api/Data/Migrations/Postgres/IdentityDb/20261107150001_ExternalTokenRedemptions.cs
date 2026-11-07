// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using Api.Data.Postgres;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Api.Data.Migrations.Postgres.IdentityDb;

[DbContext(typeof(PostgresIdentityDbContext))]
[Migration("20261107150001_ExternalTokenRedemptions")]
public sealed class ExternalTokenRedemptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ExternalTokenRedemptions",
            columns: table => new
            {
                Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                TokenId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                RedeemedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ExternalTokenRedemptions", x => new { x.Provider, x.TokenId });
            });

        migrationBuilder.CreateIndex(
            name: "IX_ExternalTokenRedemptions_ExpiresAtUtc",
            table: "ExternalTokenRedemptions",
            column: "ExpiresAtUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ExternalTokenRedemptions");
    }
}
