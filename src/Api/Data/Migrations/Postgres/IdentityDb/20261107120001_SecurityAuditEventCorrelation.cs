// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.Postgres.IdentityDb
{
    [DbContext(typeof(Api.Data.Postgres.PostgresIdentityDbContext))]
    [Migration("20261107120001_SecurityAuditEventCorrelation")]
    public class SecurityAuditEventCorrelation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "SecurityAuditEvents",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "SecurityAuditEvents",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAuditEvents_CompanyId_CreatedAt",
                table: "SecurityAuditEvents",
                columns: new[] { "CompanyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAuditEvents_EventType_CreatedAt",
                table: "SecurityAuditEvents",
                columns: new[] { "EventType", "CreatedAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SecurityAuditEvents_CompanyId_CreatedAt",
                table: "SecurityAuditEvents");

            migrationBuilder.DropIndex(
                name: "IX_SecurityAuditEvents_EventType_CreatedAt",
                table: "SecurityAuditEvents");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "SecurityAuditEvents");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "SecurityAuditEvents");
        }
    }
}
