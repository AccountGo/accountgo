using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace Api.Data.Schema
{
    public static class SqliteSchemaUpgrader
    {
        public static readonly string[] TenantTables = new[]
        {
            "Account",
            "Address",
            "AuditLog",
            "Bank",
            "CompanySetting",
            "Contact",
            "Customer",
            "CustomerAllocation",
            "CustomerContact",
            "DonationInvoiceHeader",
            "DonationInvoiceLine",
            "Donors",
            "FinancialYear",
            "GeneralLedgerHeader",
            "GeneralLedgerLine",
            "GeneralLedgerSetting",
            "InventoryControlJournal",
            "InventoryAdjustment",
            "Item",
            "ItemCategory",
            "ItemTaxGroup",
            "ItemTaxGroupTax",
            "JournalEntryHeader",
            "JournalEntryLine",
            "Log",
            "MainContraAccount",
            "Measurement",
            "Party",
            "PaymentTerm",
            "PurchaseInvoiceHeader",
            "PurchaseInvoiceLine",
            "PurchaseOrderHeader",
            "PurchaseOrderLine",
            "PurchaseReceiptHeader",
            "PurchaseReceiptLine",
            "SalesDeliveryHeader",
            "SalesDeliveryLine",
            "SalesInvoiceHeader",
            "SalesInvoiceLine",
            "SalesOrderHeader",
            "SalesOrderLine",
            "SalesQuoteHeader",
            "SalesQuoteLine",
            "SalesReceiptHeader",
            "SalesReceiptLine",
            "SequenceNumber",
            "Tax",
            "TaxGroup",
            "TaxGroupTax",
            "Vendor",
            "VendorContact",
            "VendorPayment"
        };

        public static void Upgrade(DbConnection connection, ILogger? logger = null)
        {
            bool wasClosed = connection.State == ConnectionState.Closed;
            if (wasClosed)
            {
                connection.Open();
            }

            try
            {
                // 1. Ensure __SchemaVersion table exists
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS ""__SchemaVersion"" (
                            ""Version"" INTEGER NOT NULL PRIMARY KEY,
                            ""AppliedAt"" TEXT NOT NULL
                        );";
                    cmd.ExecuteNonQuery();
                }

                // 2. Check current version
                int currentVersion = 0;
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT IFNULL(MAX(\"Version\"), 0) FROM \"__SchemaVersion\";";
                    var val = cmd.ExecuteScalar();
                    if (val != null && val != DBNull.Value)
                    {
                        currentVersion = Convert.ToInt32(val);
                    }
                }

                if (currentVersion < 1)
                {
                    logger?.LogInformation("Applying SQLite Schema Upgrade Version 1 (Multi-Tenancy DDL)...");
                    using var tx = connection.BeginTransaction();
                    try
                    {
                        ApplyVersion1(connection, tx, logger);

                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "INSERT INTO \"__SchemaVersion\" (\"Version\", \"AppliedAt\") VALUES (1, $appliedAt);";
                            var param = cmd.CreateParameter();
                            param.ParameterName = "$appliedAt";
                            param.Value = DateTime.UtcNow.ToString("o");
                            cmd.Parameters.Add(param);
                            cmd.ExecuteNonQuery();
                        }
                        tx.Commit();
                        logger?.LogInformation("✓ SQLite Schema Upgrade Version 1 applied successfully.");
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }

                // Ensure all indexes exist idempotently even if v1 was previously applied
                EnsureAllIndexes(connection, logger);
                EnsureLifecycleColumns(connection);
                EnsureTables(connection);
            }
            finally
            {
                if (wasClosed)
                {
                    connection.Close();
                }
            }
        }

        private static void EnsureLifecycleColumns(DbConnection connection)
        {
            using (var tx = connection.BeginTransaction())
            {
                var columns = new[]
                {
                    new[] { "Company", "DeletedAt", "TEXT NULL" },
                    new[] { "CompanySetting", "Currency", "TEXT NOT NULL DEFAULT 'PHP'" },
                    new[] { "CompanyInvitation", "ResendWindowStart", "TEXT NULL" },
                    new[] { "CompanyInvitation", "ResendCount", "INTEGER NOT NULL DEFAULT 0" },
                    new[] { "CompanySetting", "LegalName", "TEXT NULL" },
                    new[] { "CompanySetting", "BusinessEmail", "TEXT NULL" },
                    new[] { "CompanySetting", "BusinessPhone", "TEXT NULL" },
                    new[] { "CompanySetting", "SalesInvoiceSettingsJson", "TEXT NULL" },
                    new[] { "CompanySetting", "BusinessAddress_Line1", "TEXT NULL" },
                    new[] { "CompanySetting", "BusinessAddress_Line2", "TEXT NULL" },
                    new[] { "CompanySetting", "BusinessAddress_City", "TEXT NULL" },
                    new[] { "CompanySetting", "BusinessAddress_Region", "TEXT NULL" },
                    new[] { "CompanySetting", "BusinessAddress_PostalCode", "TEXT NULL" },
                    new[] { "CompanySetting", "BusinessAddress_CountryCode", "TEXT NULL" },
                    new[] { "SalesInvoiceHeader", "Notes", "TEXT NULL" },
                    new[] { "SalesInvoiceHeader", "PaymentInstructions", "TEXT NULL" },
                    new[] { "SalesInvoiceHeader", "SellerJson", "TEXT NULL" },
                    new[] { "SalesInvoiceHeader", "DueDate", "TEXT NULL" },
                    new[] { "SalesInvoiceHeader", "BillToJson", "TEXT NULL" },
                    new[] { "SalesInvoiceLine", "Description", "TEXT NULL" },
                    new[] { "Customer", "ArchivedAt", "TEXT NULL" },
                    new[] { "Item", "ArchivedAt", "TEXT NULL" },
                    new[] { "Item", "QuantityOnHand", "decimal(18, 4) NOT NULL DEFAULT 0" },
                    new[] { "Item", "AverageCost", "decimal(18, 4) NOT NULL DEFAULT 0" },
                    new[] { "Item", "StockValue", "decimal(18, 2) NOT NULL DEFAULT 0" },
                    new[] { "InventoryControlJournal", "DocumentId", "INTEGER NULL" },
                    new[] { "InventoryControlJournal", "UnitCost", "decimal(18, 4) NULL" },
                    new[] { "InventoryControlJournal", "Memo", "TEXT NULL" },
                    new[] { "PurchaseInvoiceHeader", "DueDate", "TEXT NULL" },
                    new[] { "PurchaseInvoiceHeader", "Notes", "TEXT NULL" },
                    new[] { "PurchaseInvoiceHeader", "BillFromJson", "TEXT NULL" },
                    new[] { "PurchaseInvoiceHeader", "VoidGeneralLedgerHeaderId", "INTEGER NULL" },
                    new[] { "PurchaseInvoiceLine", "Description", "TEXT NULL" },
                    new[] { "PurchaseInvoiceLine", "AccountId", "INTEGER NULL" },
                    new[] { "PurchaseInvoiceLine", "TaxId", "INTEGER NULL" },
                    new[] { "PurchaseInvoiceLine", "TaxAmount", "decimal(18, 2) NOT NULL DEFAULT 0" },
                    new[] { "Vendor", "ArchivedAt", "TEXT NULL" },
                    new[] { "VendorPayment", "AccountId", "INTEGER NULL" },
                    new[] { "VendorPayment", "PaymentMethod", "TEXT NULL" },
                    new[] { "VendorPayment", "ReferenceNo", "TEXT NULL" },
                    new[] { "VendorPayment", "Memo", "TEXT NULL" },
                    new[] { "SalesInvoiceHeader", "VoidGeneralLedgerHeaderId", "INTEGER NULL" }
                };
                var added = new HashSet<string>();
                foreach (var column in columns)
                {
                    if (TableExists(connection, column[0], tx) && !ColumnExists(connection, column[0], column[1], tx))
                    {
                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = $"ALTER TABLE \"{column[0]}\" ADD COLUMN \"{column[1]}\" {column[2]};";
                            cmd.ExecuteNonQuery();
                        }
                        added.Add($"{column[0]}.{column[1]}");
                    }
                }
                // Filled once, when the column arrives, so later invoices only ever get what was recorded at the time.
                if (added.Contains("Item.StockValue"))
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            UPDATE ""Item"" SET ""StockValue"" = ROUND(CASE WHEN NOT EXISTS (
                                SELECT 1 FROM ""InventoryControlJournal"" j
                                WHERE j.""ItemId"" = ""Item"".""Id"" AND j.""CompanyId"" = ""Item"".""CompanyId"" AND j.""TotalCost"" IS NULL
                            ) THEN COALESCE((
                                SELECT SUM(CASE WHEN COALESCE(j.""INQty"", 0) > 0 THEN j.""TotalCost"" ELSE -j.""TotalCost"" END)
                                FROM ""InventoryControlJournal"" j
                                WHERE j.""ItemId"" = ""Item"".""Id"" AND j.""CompanyId"" = ""Item"".""CompanyId""
                            ), ""QuantityOnHand"" * ""AverageCost"")
                            ELSE ""QuantityOnHand"" * ""AverageCost"" END, 2);";
                        cmd.ExecuteNonQuery();
                    }
                }
                if (added.Contains("SalesInvoiceHeader.BillToJson") && added.Contains("SalesInvoiceLine.Description"))
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = InvoiceHistoryBackfillSql;
                    cmd.ExecuteNonQuery();
                }
                if (added.Contains("PurchaseInvoiceLine.TaxAmount") && added.Contains("SalesInvoiceHeader.VoidGeneralLedgerHeaderId"))
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = BillsBackfillSql;
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
        }

        // D1 0012_bills.sql, without the permission rows that startup adds. Bills saved before then: posted ones are
        // approved, the rest drafts; lines keep the item's text and posted lines their inventory account. Invoices voided
        // before then get the entry reversing their posting, dated as the original.
        private const string BillsBackfillSql = @"
            UPDATE ""PurchaseInvoiceHeader"" SET ""Status"" = CASE WHEN ""GeneralLedgerHeaderId"" IS NULL THEN 0 ELSE 1 END
            WHERE ""Status"" IS NULL;
            UPDATE ""PurchaseInvoiceLine"" SET ""Description"" = (
                SELECT ""Item"".""Description"" FROM ""Item"" WHERE ""Item"".""Id"" = ""PurchaseInvoiceLine"".""ItemId"")
            WHERE ""Description"" IS NULL;
            UPDATE ""PurchaseInvoiceLine"" SET ""AccountId"" = (
                SELECT ""Item"".""InventoryAccountId"" FROM ""Item"" WHERE ""Item"".""Id"" = ""PurchaseInvoiceLine"".""ItemId"")
            WHERE ""AccountId"" IS NULL
              AND ""PurchaseInvoiceHeaderId"" IN (SELECT ""Id"" FROM ""PurchaseInvoiceHeader"" WHERE ""GeneralLedgerHeaderId"" IS NOT NULL);
            INSERT INTO ""GeneralLedgerHeader"" (""CompanyId"", ""Date"", ""DocumentType"", ""Description"")
            SELECT i.""CompanyId"", g.""Date"", g.""DocumentType"", 'Void of invoice ' || COALESCE(i.""No"", i.""Id"")
            FROM ""SalesInvoiceHeader"" i JOIN ""GeneralLedgerHeader"" g ON g.""Id"" = i.""GeneralLedgerHeaderId""
            WHERE i.""Status"" = 4 AND i.""VoidGeneralLedgerHeaderId"" IS NULL;
            UPDATE ""SalesInvoiceHeader"" SET ""VoidGeneralLedgerHeaderId"" = (
                SELECT MAX(g.""Id"") FROM ""GeneralLedgerHeader"" g
                WHERE g.""CompanyId"" = ""SalesInvoiceHeader"".""CompanyId""
                  AND g.""Description"" = 'Void of invoice ' || COALESCE(""SalesInvoiceHeader"".""No"", ""SalesInvoiceHeader"".""Id""))
            WHERE ""Status"" = 4 AND ""GeneralLedgerHeaderId"" IS NOT NULL AND ""VoidGeneralLedgerHeaderId"" IS NULL;
            INSERT INTO ""GeneralLedgerLine"" (""CompanyId"", ""GeneralLedgerHeaderId"", ""AccountId"", ""DrCr"", ""Amount"")
            SELECT l.""CompanyId"", i.""VoidGeneralLedgerHeaderId"", l.""AccountId"", CASE l.""DrCr"" WHEN 1 THEN 2 ELSE 1 END, l.""Amount""
            FROM ""SalesInvoiceHeader"" i JOIN ""GeneralLedgerLine"" l ON l.""GeneralLedgerHeaderId"" = i.""GeneralLedgerHeaderId""
            WHERE i.""VoidGeneralLedgerHeaderId"" IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM ""GeneralLedgerLine"" x WHERE x.""GeneralLedgerHeaderId"" = i.""VoidGeneralLedgerHeaderId"");";

        // D1 0009_invoice_history.sql. Customers and items could not be edited before these columns existed, so what they
        // say now is what the existing invoices were issued with. Issued invoices (Open, Overdue, Closed, and posted ones
        // that were voided or predate statuses) get a bill-to; drafts keep following the customer.
        private const string InvoiceHistoryBackfillSql = @"
            UPDATE ""SalesInvoiceLine"" SET ""Description"" = (
                SELECT ""Item"".""Description"" FROM ""Item"" WHERE ""Item"".""Id"" = ""SalesInvoiceLine"".""ItemId"")
            WHERE ""Description"" IS NULL;
            UPDATE ""SalesInvoiceHeader"" SET ""BillToJson"" = (
                SELECT json_object(
                    'name', COALESCE(""Party"".""Name"", 'Unknown'),
                    'email', ""Party"".""Email"",
                    'phone', ""Party"".""Phone"",
                    'addressLines', json_array(""Party"".""CompanyStreet"", ""Party"".""CompanyProvince"", ""Party"".""CompanyPostalCode"", ""Party"".""CompanyCountry""))
                FROM ""Customer"" JOIN ""Party"" ON ""Party"".""Id"" = ""Customer"".""PartyId""
                WHERE ""Customer"".""Id"" = ""SalesInvoiceHeader"".""CustomerId"")
            WHERE ""BillToJson"" IS NULL
              AND (""Status"" IN (1, 2, 3) OR ((""Status"" IS NULL OR ""Status"" = 4) AND ""GeneralLedgerHeaderId"" IS NOT NULL));";

        // Tables added after the first release; the same statements are in the D1 migrations.
        private static void EnsureTables(DbConnection connection)
        {
            if (!TableExists(connection, "SalesInvoiceHeader")
                || !TableExists(connection, "Item")
                || !TableExists(connection, "GeneralLedgerHeader")
                || !TableExists(connection, "InventoryControlJournal"))
                return;

            using var cmd = connection.CreateCommand();
            cmd.CommandText = SalesInvoiceEmailTableSql;
            cmd.ExecuteNonQuery();
            cmd.CommandText = InventoryAdjustmentTableSql;
            cmd.ExecuteNonQuery();
        }

        // D1 0006_invoice_emails.sql
        private const string SalesInvoiceEmailTableSql = @"
            CREATE TABLE IF NOT EXISTS ""SalesInvoiceEmail"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_SalesInvoiceEmail"" PRIMARY KEY AUTOINCREMENT,
                ""CompanyId"" INTEGER NOT NULL,
                ""SalesInvoiceHeaderId"" INTEGER NOT NULL,
                ""Kind"" TEXT NOT NULL,
                ""ToEmail"" TEXT NOT NULL,
                ""BccEmail"" TEXT NULL,
                ""Subject"" TEXT NOT NULL,
                ""SentAt"" TEXT NOT NULL,
                ""SentByUserId"" TEXT NULL,
                ""SentByEmail"" TEXT NULL,
                ""ProviderMessageId"" TEXT NULL,
                CONSTRAINT ""FK_SalesInvoiceEmail_SalesInvoiceHeader_SalesInvoiceHeaderId"" FOREIGN KEY (""SalesInvoiceHeaderId"") REFERENCES ""SalesInvoiceHeader"" (""Id"") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ""IX_SalesInvoiceEmail_CompanyId_SentAt"" ON ""SalesInvoiceEmail"" (""CompanyId"", ""SentAt"");
            CREATE INDEX IF NOT EXISTS ""IX_SalesInvoiceEmail_SalesInvoiceHeaderId"" ON ""SalesInvoiceEmail"" (""SalesInvoiceHeaderId"");";

        private const string InventoryAdjustmentTableSql = @"
            CREATE TABLE IF NOT EXISTS ""InventoryAdjustment"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_InventoryAdjustment"" PRIMARY KEY AUTOINCREMENT,
                ""CompanyId"" INTEGER NOT NULL,
                ""No"" TEXT NOT NULL,
                ""Date"" TEXT NOT NULL,
                ""ItemId"" INTEGER NOT NULL,
                ""QuantityChange"" decimal(18, 4) NOT NULL,
                ""UnitCost"" decimal(18, 4) NULL,
                ""Reason"" TEXT NOT NULL,
                ""Memo"" TEXT NULL,
                ""GeneralLedgerHeaderId"" INTEGER NULL,
                ""InventoryControlJournalId"" INTEGER NULL,
                CONSTRAINT ""FK_InventoryAdjustment_Item_ItemId"" FOREIGN KEY (""ItemId"") REFERENCES ""Item"" (""Id""),
                CONSTRAINT ""FK_InventoryAdjustment_GeneralLedgerHeader_GeneralLedgerHeaderId"" FOREIGN KEY (""GeneralLedgerHeaderId"") REFERENCES ""GeneralLedgerHeader"" (""Id""),
                CONSTRAINT ""FK_InventoryAdjustment_InventoryControlJournal_InventoryControlJournalId"" FOREIGN KEY (""InventoryControlJournalId"") REFERENCES ""InventoryControlJournal"" (""Id"")
            );
            CREATE INDEX IF NOT EXISTS ""IX_InventoryAdjustment_CompanyId"" ON ""InventoryAdjustment"" (""CompanyId"");
            CREATE INDEX IF NOT EXISTS ""IX_InventoryAdjustment_ItemId"" ON ""InventoryAdjustment"" (""ItemId"");
            CREATE INDEX IF NOT EXISTS ""IX_InventoryAdjustment_GeneralLedgerHeaderId"" ON ""InventoryAdjustment"" (""GeneralLedgerHeaderId"");
            CREATE INDEX IF NOT EXISTS ""IX_InventoryAdjustment_InventoryControlJournalId"" ON ""InventoryAdjustment"" (""InventoryControlJournalId"");";

        public static void UpgradePostSeed(DbConnection connection, ILogger? logger = null)
        {
            bool wasClosed = connection.State == ConnectionState.Closed;
            if (wasClosed)
            {
                connection.Open();
            }

            try
            {
                if (!TableExists(connection, "__SchemaVersion"))
                {
                    return;
                }

                int currentVersion = 0;
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT IFNULL(MAX(\"Version\"), 0) FROM \"__SchemaVersion\";";
                    var val = cmd.ExecuteScalar();
                    if (val != null && val != DBNull.Value)
                    {
                        currentVersion = Convert.ToInt32(val);
                    }
                }

                if (currentVersion < 2)
                {
                    logger?.LogInformation("Applying SQLite Schema Upgrade Version 2 (Membership & Role Backfill)...");
                    using var tx = connection.BeginTransaction();
                    try
                    {
                        ApplyVersion2(connection, tx, logger);

                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "INSERT INTO \"__SchemaVersion\" (\"Version\", \"AppliedAt\") VALUES (2, $appliedAt);";
                            var param = cmd.CreateParameter();
                            param.ParameterName = "$appliedAt";
                            param.Value = DateTime.UtcNow.ToString("o");
                            cmd.Parameters.Add(param);
                            cmd.ExecuteNonQuery();
                        }
                        tx.Commit();
                        logger?.LogInformation("✓ SQLite Schema Upgrade Version 2 applied successfully.");
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
            finally
            {
                if (wasClosed)
                {
                    connection.Close();
                }
            }
        }

        private static void ApplyVersion1(DbConnection connection, DbTransaction tx, ILogger? logger)
        {
            // 1. Ensure CompanyId exists on all tenant tables
            foreach (var table in TenantTables)
            {
                if (!TableExists(connection, table, tx))
                {
                    continue;
                }

                if (!ColumnExists(connection, table, "CompanyId", tx))
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"CompanyId\" INTEGER NOT NULL DEFAULT 1;";
                    cmd.ExecuteNonQuery();
                    logger?.LogInformation("Added CompanyId to table {Table}", table);
                }
            }

            // 1b. Backfill GeneralLedgerSetting.CompanyId NULLs to 1 (Item 7 gap)
            if (TableExists(connection, "GeneralLedgerSetting", tx))
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE \"GeneralLedgerSetting\" SET \"CompanyId\" = 1 WHERE \"CompanyId\" IS NULL;";
                cmd.ExecuteNonQuery();
            }

            // 2. Add CreatedAt and CreatedByUserId to Company table
            if (TableExists(connection, "Company", tx))
            {
                if (!ColumnExists(connection, "Company", "CreatedAt", tx))
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = "ALTER TABLE \"Company\" ADD COLUMN \"CreatedAt\" TEXT NOT NULL DEFAULT '2025-01-01T00:00:00Z';";
                    cmd.ExecuteNonQuery();
                }

                if (!ColumnExists(connection, "Company", "CreatedByUserId", tx))
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = "ALTER TABLE \"Company\" ADD COLUMN \"CreatedByUserId\" TEXT NULL;";
                    cmd.ExecuteNonQuery();
                }
            }

            // 3. Create Tenancy tables
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ""CompanyMember"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_CompanyMember"" PRIMARY KEY AUTOINCREMENT,
                        ""CompanyId"" INTEGER NOT NULL,
                        ""UserId"" TEXT NOT NULL,
                        ""Role"" INTEGER NOT NULL,
                        ""Status"" INTEGER NOT NULL,
                        ""JoinedAt"" TEXT NOT NULL,
                        ""InvitedByUserId"" TEXT NULL,
                        CONSTRAINT ""FK_CompanyMember_Company_CompanyId"" FOREIGN KEY (""CompanyId"") REFERENCES ""Company"" (""Id"") ON DELETE CASCADE
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CompanyMember_CompanyId_UserId"" ON ""CompanyMember"" (""CompanyId"", ""UserId"");
                    CREATE INDEX IF NOT EXISTS ""IX_CompanyMember_UserId"" ON ""CompanyMember"" (""UserId"");

                    CREATE TABLE IF NOT EXISTS ""CompanyMemberSecurityRole"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_CompanyMemberSecurityRole"" PRIMARY KEY AUTOINCREMENT,
                        ""CompanyMemberId"" INTEGER NOT NULL,
                        ""SecurityRoleId"" INTEGER NOT NULL,
                        CONSTRAINT ""FK_CompanyMemberSecurityRole_CompanyMember_CompanyMemberId"" FOREIGN KEY (""CompanyMemberId"") REFERENCES ""CompanyMember"" (""Id"") ON DELETE CASCADE,
                        CONSTRAINT ""FK_CompanyMemberSecurityRole_SecurityRole_SecurityRoleId"" FOREIGN KEY (""SecurityRoleId"") REFERENCES ""SecurityRole"" (""Id"") ON DELETE CASCADE
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CompanyMemberSecurityRole_CompanyMemberId_SecurityRoleId"" ON ""CompanyMemberSecurityRole"" (""CompanyMemberId"", ""SecurityRoleId"");
                    CREATE INDEX IF NOT EXISTS ""IX_CompanyMemberSecurityRole_SecurityRoleId"" ON ""CompanyMemberSecurityRole"" (""SecurityRoleId"");

                    CREATE TABLE IF NOT EXISTS ""CompanyInvitation"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_CompanyInvitation"" PRIMARY KEY AUTOINCREMENT,
                        ""CompanyId"" INTEGER NOT NULL,
                        ""Email"" TEXT NOT NULL,
                        ""Role"" INTEGER NOT NULL,
                        ""SecurityRoleIdsJson"" TEXT NOT NULL DEFAULT '[]',
                        ""TokenHash"" TEXT NOT NULL,
                        ""ExpiresAt"" TEXT NOT NULL,
                        ""CreatedAt"" TEXT NOT NULL,
                        ""InvitedByUserId"" TEXT NOT NULL,
                        ""AcceptedAt"" TEXT NULL,
                        ""AcceptedByUserId"" TEXT NULL,
                        ""RevokedAt"" TEXT NULL,
                        CONSTRAINT ""FK_CompanyInvitation_Company_CompanyId"" FOREIGN KEY (""CompanyId"") REFERENCES ""Company"" (""Id"") ON DELETE CASCADE
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CompanyInvitation_TokenHash"" ON ""CompanyInvitation"" (""TokenHash"");
                    CREATE INDEX IF NOT EXISTS ""IX_CompanyInvitation_CompanyId_Email"" ON ""CompanyInvitation"" (""CompanyId"", ""Email"");
                ";
                cmd.ExecuteNonQuery();
            }

            // 4. Create composite unique indexes on tenant tables
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    DROP INDEX IF EXISTS ""IX_Account_CompanyId"";
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Account_CompanyId_AccountCode"" ON ""Account"" (""CompanyId"", ""AccountCode"");
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SequenceNumber_CompanyId_SequenceNumberType"" ON ""SequenceNumber"" (""CompanyId"", ""SequenceNumberType"");
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Customer_CompanyId_No"" ON ""Customer"" (""CompanyId"", ""No"");
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Vendor_CompanyId_No"" ON ""Vendor"" (""CompanyId"", ""No"");
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Item_CompanyId_No"" ON ""Item"" (""CompanyId"", ""No"");
                ";
                cmd.ExecuteNonQuery();
            }
        }

        private static void EnsureAllIndexes(DbConnection connection, ILogger? logger)
        {
            if (TableExists(connection, "CompanyMemberSecurityRole"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CompanyMemberSecurityRole_CompanyMemberId_SecurityRoleId"" ON ""CompanyMemberSecurityRole"" (""CompanyMemberId"", ""SecurityRoleId"");
                    CREATE INDEX IF NOT EXISTS ""IX_CompanyMemberSecurityRole_SecurityRoleId"" ON ""CompanyMemberSecurityRole"" (""SecurityRoleId"");
                ";
                cmd.ExecuteNonQuery();
            }

            if (TableExists(connection, "CompanyMember"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CompanyMember_CompanyId_UserId"" ON ""CompanyMember"" (""CompanyId"", ""UserId"");
                    CREATE INDEX IF NOT EXISTS ""IX_CompanyMember_UserId"" ON ""CompanyMember"" (""UserId"");
                ";
                cmd.ExecuteNonQuery();
            }

            if (TableExists(connection, "CompanyInvitation"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CompanyInvitation_TokenHash"" ON ""CompanyInvitation"" (""TokenHash"");
                    CREATE INDEX IF NOT EXISTS ""IX_CompanyInvitation_CompanyId_Email"" ON ""CompanyInvitation"" (""CompanyId"", ""Email"");
                ";
                cmd.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
                    DROP INDEX IF EXISTS ""IX_Account_CompanyId"";
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Account_CompanyId_AccountCode"" ON ""Account"" (""CompanyId"", ""AccountCode"");
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SequenceNumber_CompanyId_SequenceNumberType"" ON ""SequenceNumber"" (""CompanyId"", ""SequenceNumberType"");
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Customer_CompanyId_No"" ON ""Customer"" (""CompanyId"", ""No"");
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Vendor_CompanyId_No"" ON ""Vendor"" (""CompanyId"", ""No"");
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Item_CompanyId_No"" ON ""Item"" (""CompanyId"", ""No"");
                ";
                cmd.ExecuteNonQuery();
            }

            if (TableExists(connection, "SalesInvoiceHeader"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = SalesInvoiceNumberIndexSql;
                cmd.ExecuteNonQuery();
            }
        }

        // D1 0008_invoice_number_unique.sql. A blank number means "not numbered yet"; NULLs never clash in a unique index.
        private const string SalesInvoiceNumberIndexSql = @"
            UPDATE ""SalesInvoiceHeader"" SET ""No"" = NULL WHERE ""No"" IS NOT NULL AND TRIM(""No"") = '';
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SalesInvoiceHeader_CompanyId_No"" ON ""SalesInvoiceHeader"" (""CompanyId"", ""No"");";

        private static void ApplyVersion2(DbConnection connection, DbTransaction tx, ILogger? logger)
        {
            if (!TableExists(connection, "AspNetUsers", tx) || !TableExists(connection, "CompanyMember", tx))
            {
                return;
            }

            string now = DateTime.UtcNow.ToString("o");

            // D1: marvinperez@gmail.com = Owner (1)
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO ""CompanyMember"" (""CompanyId"", ""UserId"", ""Role"", ""Status"", ""JoinedAt"")
                    SELECT 1, ""Id"", 1, 1, $now
                    FROM ""AspNetUsers""
                    WHERE LOWER(""Email"") = 'marvinperez@gmail.com'
                      AND NOT EXISTS (
                          SELECT 1 FROM ""CompanyMember""
                          WHERE ""CompanyId"" = 1 AND ""UserId"" = ""AspNetUsers"".""Id""
                      );";
                var p = cmd.CreateParameter();
                p.ParameterName = "$now";
                p.Value = now;
                cmd.Parameters.Add(p);
                int inserted = cmd.ExecuteNonQuery();
                if (inserted > 0)
                {
                    logger?.LogInformation("✓ Backfilled Owner membership for marvinperez@gmail.com in Company 1.");
                }
            }

            // D1: hello@marvinperez.com = Admin (2)
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO ""CompanyMember"" (""CompanyId"", ""UserId"", ""Role"", ""Status"", ""JoinedAt"")
                    SELECT 1, ""Id"", 2, 1, $now
                    FROM ""AspNetUsers""
                    WHERE LOWER(""Email"") = 'hello@marvinperez.com'
                      AND NOT EXISTS (
                          SELECT 1 FROM ""CompanyMember""
                          WHERE ""CompanyId"" = 1 AND ""UserId"" = ""AspNetUsers"".""Id""
                      );";
                var p = cmd.CreateParameter();
                p.ParameterName = "$now";
                p.Value = now;
                cmd.Parameters.Add(p);
                int inserted = cmd.ExecuteNonQuery();
                if (inserted > 0)
                {
                    logger?.LogInformation("✓ Backfilled Admin membership for hello@marvinperez.com in Company 1.");
                }
            }

            // D1: admin@accountgo.ph = Member (3)
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO ""CompanyMember"" (""CompanyId"", ""UserId"", ""Role"", ""Status"", ""JoinedAt"")
                    SELECT 1, ""Id"", 3, 1, $now
                    FROM ""AspNetUsers""
                    WHERE LOWER(""Email"") = 'admin@accountgo.ph'
                      AND NOT EXISTS (
                          SELECT 1 FROM ""CompanyMember""
                          WHERE ""CompanyId"" = 1 AND ""UserId"" = ""AspNetUsers"".""Id""
                      );";
                var p = cmd.CreateParameter();
                p.ParameterName = "$now";
                p.Value = now;
                cmd.Parameters.Add(p);
                int inserted = cmd.ExecuteNonQuery();
                if (inserted > 0)
                {
                    logger?.LogInformation("✓ Backfilled Member membership for admin@accountgo.ph in Company 1.");
                }
            }

            // D1: Attach 'Auditor / Viewer' role to admin@accountgo.ph's CompanyMember
            if (TableExists(connection, "CompanyMemberSecurityRole", tx) && TableExists(connection, "SecurityRole", tx))
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO ""CompanyMemberSecurityRole"" (""CompanyMemberId"", ""SecurityRoleId"")
                    SELECT cm.""Id"", sr.""Id""
                    FROM ""CompanyMember"" cm
                    JOIN ""AspNetUsers"" u ON u.""Id"" = cm.""UserId""
                    JOIN ""SecurityRole"" sr ON sr.""Name"" = 'Auditor / Viewer'
                    WHERE cm.""CompanyId"" = 1 AND LOWER(u.""Email"") = 'admin@accountgo.ph'
                      AND NOT EXISTS (
                          SELECT 1 FROM ""CompanyMemberSecurityRole"" cmsr
                          WHERE cmsr.""CompanyMemberId"" = cm.""Id"" AND cmsr.""SecurityRoleId"" = sr.""Id""
                      );";
                int roleAttached = cmd.ExecuteNonQuery();
                if (roleAttached > 0)
                {
                    logger?.LogInformation("✓ Attached 'Auditor / Viewer' role to admin@accountgo.ph in Company 1.");
                }
            }

            // D2: Remove SystemAdministrators from SecurityUserRole for users other than marvinperez@gmail.com
            if (TableExists(connection, "SecurityUserRole", tx) && TableExists(connection, "SecurityRole", tx) && TableExists(connection, "User", tx))
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    DELETE FROM ""SecurityUserRole""
                    WHERE ""SecurityRoleId"" IN (SELECT ""Id"" FROM ""SecurityRole"" WHERE ""Name"" = 'SystemAdministrators')
                      AND ""UserId"" NOT IN (
                          SELECT ""Id"" FROM ""User""
                          WHERE LOWER(""UserName"") = 'marvinperez@gmail.com' OR LOWER(""EmailAddress"") = 'marvinperez@gmail.com'
                      );";
                int removed = cmd.ExecuteNonQuery();
                if (removed > 0)
                {
                    logger?.LogInformation("✓ Removed SystemAdministrators role from non-platform staff ({Count} records).", removed);
                }
            }
        }

        private static bool TableExists(DbConnection connection, string tableName, DbTransaction? tx = null)
        {
            using var cmd = connection.CreateCommand();
            if (tx != null) cmd.Transaction = tx;
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$tableName;";
            var p = cmd.CreateParameter();
            p.ParameterName = "$tableName";
            p.Value = tableName;
            cmd.Parameters.Add(p);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        private static bool ColumnExists(DbConnection connection, string tableName, string columnName, DbTransaction? tx = null)
        {
            using var cmd = connection.CreateCommand();
            if (tx != null) cmd.Transaction = tx;
            cmd.CommandText = $"PRAGMA table_info(\"{tableName}\");";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var col = reader.GetString(1);
                if (string.Equals(col, columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
