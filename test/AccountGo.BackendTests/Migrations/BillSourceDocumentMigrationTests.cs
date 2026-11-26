// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Linq;
using Api.Data;
using Api.Data.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AccountGo.BackendTests.Migrations;

public sealed class BillSourceDocumentMigrationTests
{
    [Fact]
    public void Upgrade_AddsNullableSourceDocumentTypeToExistingDatabase_WithoutBackfillingOrInferringFromCaptures()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using (var db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options))
        {
            db.Database.EnsureCreated();
        }

        // Simulate a pre-migration database: drop SourceDocumentType from PurchaseInvoiceHeader and seed a bill + linked expense-receipt capture.
        Execute(connection, """
            PRAGMA foreign_keys = OFF;
            ALTER TABLE "PurchaseInvoiceHeader" DROP COLUMN "SourceDocumentType";
            INSERT INTO "PurchaseInvoiceHeader" ("Id", "CompanyId", "PublicId", "Date", "VendorInvoiceNo", "CreditedAmount", "AdvanceAppliedAmount", "Version")
            VALUES (701, 1, 'bill_01legacy0000000000000001', '2026-11-01T00:00:00', 'LEGACY-701', 0, 0, 1);
            INSERT INTO "DocumentCaptures" ("Id", "CompanyId", "PublicId", "Kind", "SchemaVersion", "Status", "ContentType", "ByteCount", "PageCount", "TextPageCount", "SourceHash", "ContentHash", "NormalizationVersion", "RequestFingerprint", "ProviderName", "ProviderVersion", "CreatedBy", "CreatedAt", "ExpiresAt", "ExtractionGeneration")
            VALUES (801, 1, 'cap_01legacy0000000000000001', 'expense-receipt', 1, 'accepted', 'image/png', 128, 1, 1, '1111111111111111111111111111111111111111111111111111111111111111', '2222222222222222222222222222222222222222222222222222222222222222', 'v2', 'fp_legacy_801', 'fake', '1', 'tester', '2026-11-01T00:00:00', '2026-11-02T00:00:00', 1);
            INSERT INTO "DocumentCaptureLinks" ("CompanyId", "CaptureId", "TargetType", "TargetId", "LinkedBy", "LinkedAt")
            VALUES (1, 801, 'bill', 701, 'tester', '2026-11-01T00:05:00');
            """);

        Assert.Equal(0, ScalarInt(connection, "SELECT COUNT(*) FROM pragma_table_info('PurchaseInvoiceHeader') WHERE name = 'SourceDocumentType';"));

        SqliteSchemaUpgrader.Upgrade(connection);
        SqliteSchemaUpgrader.Upgrade(connection);

        Assert.Equal(1, ScalarInt(connection, "SELECT COUNT(*) FROM pragma_table_info('PurchaseInvoiceHeader') WHERE name = 'SourceDocumentType' AND \"notnull\" = 0;"));
        Assert.Equal(1, ScalarInt(connection, "SELECT COUNT(*) FROM \"PurchaseInvoiceHeader\" WHERE \"Id\" = 701 AND \"SourceDocumentType\" IS NULL;"));

        using (var db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options))
        {
            var persisted = db.PurchaseInvoiceHeaders.IgnoreQueryFilters().AsNoTracking().Single(b => b.Id == 701);
            Assert.Null(persisted.SourceDocumentType);
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static int ScalarInt(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
