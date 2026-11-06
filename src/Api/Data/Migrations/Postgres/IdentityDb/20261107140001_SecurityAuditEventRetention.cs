// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Api.Data.Postgres;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Api.Data.Migrations.Postgres.IdentityDb;

[DbContext(typeof(PostgresIdentityDbContext))]
[Migration("20261107140001_SecurityAuditEventRetention")]
public sealed class SecurityAuditEventRetention : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION "RejectSecurityAuditEventMutation"() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
              IF TG_OP = 'UPDATE' THEN
                RAISE EXCEPTION 'SecurityAuditEvents rows cannot be updated';
              END IF;

              IF (OLD."EventType" LIKE 'data.%' OR OLD."EventType" = 'invoice.pdf.public') THEN
                IF OLD."CreatedAt" >= ((now() AT TIME ZONE 'utc') - INTERVAL '365 days') THEN
                  RAISE EXCEPTION 'SecurityAuditEvents cannot be deleted before the retention floor';
                END IF;
              ELSE
                IF OLD."CreatedAt" >= ((now() AT TIME ZONE 'utc') - INTERVAL '730 days') THEN
                  RAISE EXCEPTION 'SecurityAuditEvents cannot be deleted before the retention floor';
                END IF;
              END IF;

              RETURN OLD;
            END;
            $$;
            """);

        migrationBuilder.Sql("""
            CREATE TRIGGER "TR_SecurityAuditEvents_Immutable"
            BEFORE UPDATE OR DELETE ON "SecurityAuditEvents"
            FOR EACH ROW EXECUTE FUNCTION "RejectSecurityAuditEventMutation"();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "TR_SecurityAuditEvents_Immutable" ON "SecurityAuditEvents";""");
        migrationBuilder.Sql("""DROP FUNCTION IF EXISTS "RejectSecurityAuditEventMutation"();""");
    }
}
