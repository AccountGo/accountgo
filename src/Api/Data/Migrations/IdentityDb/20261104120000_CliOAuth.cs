// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.IdentityDb
{
    /// <summary>
    /// CLI OAuth: channel columns on RefreshTokens + OAuthAuthorizationRequests table.
    /// </summary>
    [DbContext(typeof(ApplicationIdentityDbContext))]
    [Migration("20261104120000_CliOAuth")]
    public class CliOAuth : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Channel",
                table: "RefreshTokens",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "web");

            migrationBuilder.AddColumn<string>(
                name: "ClientId",
                table: "RefreshTokens",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeviceLabel",
                table: "RefreshTokens",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Scope",
                table: "RefreshTokens",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AbsoluteExpiresAt",
                table: "RefreshTokens",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_Channel",
                table: "RefreshTokens",
                column: "Channel");

            migrationBuilder.CreateTable(
                name: "OAuthAuthorizationRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RedirectUri = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CodeChallenge = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    DeviceLabel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CodeHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IssuedRefreshFamilyId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeniedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthAuthorizationRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OAuthAuthorizationRequests_CodeHash",
                table: "OAuthAuthorizationRequests",
                column: "CodeHash");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthAuthorizationRequests_ExpiresAt",
                table: "OAuthAuthorizationRequests",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthAuthorizationRequests_ClientId",
                table: "OAuthAuthorizationRequests",
                column: "ClientId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "OAuthAuthorizationRequests");
            migrationBuilder.DropIndex(name: "IX_RefreshTokens_Channel", table: "RefreshTokens");
            migrationBuilder.DropColumn(name: "AbsoluteExpiresAt", table: "RefreshTokens");
            migrationBuilder.DropColumn(name: "Scope", table: "RefreshTokens");
            migrationBuilder.DropColumn(name: "DeviceLabel", table: "RefreshTokens");
            migrationBuilder.DropColumn(name: "ClientId", table: "RefreshTokens");
            migrationBuilder.DropColumn(name: "Channel", table: "RefreshTokens");
        }
    }
}
