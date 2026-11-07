// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Api.Data.Migrations.IdentityDb;

[DbContext(typeof(ApplicationIdentityDbContext))]
[Migration("20261107150000_ExternalTokenRedemptions")]
public sealed class ExternalTokenRedemptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ExternalTokenRedemptions",
            columns: table => new
            {
                Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                TokenId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                RedeemedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
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
