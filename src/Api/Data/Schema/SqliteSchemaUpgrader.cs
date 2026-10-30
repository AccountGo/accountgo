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
            "BankTransaction",
            "CompanySetting",
            "Contact",
            "Customer",
            "CustomerAllocation",
            "CustomerContact",
            "DonationInvoiceHeader",
            "DonationInvoiceLine",
            "Donors",
            "EntitlementUsage",
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
                // 0. Enable WAL and sane durability before any schema/data work.
                using (var pragmaCmd = connection.CreateCommand())
                {
                    pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
                    pragmaCmd.ExecuteNonQuery();
                }

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
                EnsureSecurityTables(connection);
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
                    new[] { "AspNetUsers", "DeletedAt", "TEXT NULL" },
                    new[] { "CompanySetting", "Currency", "TEXT NOT NULL DEFAULT 'PHP'" },
                    new[] { "CompanySetting", "ReportingFramework", "INTEGER NOT NULL DEFAULT 1" },
                    new[] { "CompanySetting", "TaxRegime", "INTEGER NOT NULL DEFAULT 0" },
                    new[] { "CompanySetting", "FiscalYearPattern", "INTEGER NOT NULL DEFAULT 1" },
                    new[] { "CompanySetting", "FiscalYearStartMonth", "INTEGER NULL" },
                    new[] { "CompanySetting", "BooksStartDate", "TEXT NOT NULL DEFAULT '1970-01-01'" },
                    new[] { "CompanySetting", "IndustryTemplate", "INTEGER NOT NULL DEFAULT 1" },
                    new[] { "CompanySetting", "InventoryCostingMethod", "INTEGER NOT NULL DEFAULT 0" },
                    new[] { "CompanySetting", "CoaTemplateId", "TEXT NOT NULL DEFAULT 'legacy-default'" },
                    new[] { "CompanySetting", "OnboardingChecklistDismissedAt", "TEXT NULL" },
                    new[] { "CompanySetting", "OnboardingChecklistJson", "TEXT NULL" },
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
                    new[] { "SalesInvoiceLine", "TaxId", "INTEGER NULL REFERENCES \"Tax\" (\"Id\")" },
                    new[] { "SalesInvoiceLine", "TaxAmount", "decimal(18, 2) NOT NULL DEFAULT 0" },
                    new[] { "SalesInvoiceLine", "SalesAccountId", "INTEGER NULL REFERENCES \"Account\" (\"Id\")" },
                    new[] { "Customer", "ArchivedAt", "TEXT NULL" },
                    // D1 0029_bill_to_contact.sql
                    new[] { "Customer", "BillToContactId", "INTEGER NULL REFERENCES \"Contact\" (\"Id\") ON DELETE SET NULL" },
                    new[] { "Vendor", "BillToContactId", "INTEGER NULL REFERENCES \"Contact\" (\"Id\") ON DELETE SET NULL" },
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
                    new[] { "SalesInvoiceHeader", "VoidGeneralLedgerHeaderId", "INTEGER NULL" },
                    new[] { "CompanySetting", "AccountingBasis", "INTEGER NOT NULL DEFAULT 1" },
                    new[] { "GeneralLedgerHeader", "SourceDocumentId", "INTEGER NULL" },
                    new[] { "GeneralLedgerHeader", "ReversedGeneralLedgerHeaderId", "INTEGER NULL" },
                    new[] { "CustomerAllocation", "GeneralLedgerHeaderId", "INTEGER NULL" },
                    new[] { "Company", "Tier", "TEXT NULL" },
                    // D1 0028_company_billing.sql
                    new[] { "Company", "BillingProvider", "TEXT NULL" },
                    new[] { "Company", "BillingCustomerId", "TEXT NULL" },
                    new[] { "Company", "BillingSubscriptionId", "TEXT NULL" },
                    new[] { "Company", "CurrentPeriodEnd", "TEXT NULL" },
                    new[] { "Company", "CancelAtPeriodEnd", "INTEGER NOT NULL DEFAULT 0" },
                    new[] { "Company", "BillingManagedManually", "INTEGER NOT NULL DEFAULT 0" },
                    // D1 0030_entitlements_e1b.sql
                    new[] { "Company", "DisabledModulesJson", "TEXT NULL" },
                    new[] { "Company", "PaidAccessEndedAt", "TEXT NULL" },
                    new[] { "Account", "ArchivedAt", "TEXT NULL" },
                    new[] { "CompanySetting", "MfaPolicy", "INTEGER NOT NULL DEFAULT 0" },
                    new[] { "CompanySetting", "MfaGracePeriodEndsAt", "TEXT NULL" },
                    new[] { "CompanySetting", "RequireMfaEveryLogin", "INTEGER NOT NULL DEFAULT 0" },
                    new[] { "AspNetUsers", "MfaReEnrollRequired", "INTEGER NOT NULL DEFAULT 0" },
                    new[] { "RefreshTokens", "MfaMethod", "TEXT NULL" },
                    new[] { "RefreshTokens", "Amr", "TEXT NULL" },
                    new[] { "RefreshTokens", "TrustedDeviceId", "INTEGER NULL" },
                    // D1 0031_refresh_token_family.sql
                    new[] { "RefreshTokens", "FamilyId", "TEXT NOT NULL DEFAULT ''" },
                    new[] { "RefreshTokens", "GraceReuseCount", "INTEGER NOT NULL DEFAULT 0" },
                    // D1 0032_bank_accounts.sql (#413 Phase 1A)
                    new[] { "Bank", "RoutingNumber", "TEXT NULL" },
                    new[] { "Bank", "Iban", "TEXT NULL" },
                    new[] { "Bank", "Swift", "TEXT NULL" },
                    new[] { "Bank", "IsDefaultForReceipts", "INTEGER NOT NULL DEFAULT 0" },
                    new[] { "Bank", "IsDefaultForPayments", "INTEGER NOT NULL DEFAULT 0" },
                    new[] { "Bank", "OpeningBalance", "NUMERIC NULL" },
                    new[] { "Bank", "OpeningBalanceDate", "TEXT NULL" },
                    new[] { "Bank", "OpeningBalanceGeneralLedgerHeaderId", "INTEGER NULL" },
                    new[] { "Bank", "ArchivedAt", "TEXT NULL" },
                    new[] { "Bank", "CurrencyCode", "TEXT NULL" },
                    // D1 0035_bill_payment_voids_and_audit.sql (#415)
                    new[] { "PurchaseInvoiceHeader", "PayableAccountId", "INTEGER NULL" },
                    new[] { "PurchaseInvoiceHeader", "ApprovedBy", "TEXT NULL" },
                    new[] { "PurchaseInvoiceHeader", "ApprovedAt", "TEXT NULL" },
                    new[] { "PurchaseInvoiceHeader", "VoidedBy", "TEXT NULL" },
                    new[] { "PurchaseInvoiceHeader", "VoidedAt", "TEXT NULL" },
                    new[] { "PurchaseInvoiceHeader", "VoidReason", "TEXT NULL" },
                    new[] { "VendorPayment", "VoidGeneralLedgerHeaderId", "INTEGER NULL" },
                    new[] { "VendorPayment", "VoidedBy", "TEXT NULL" },
                    new[] { "VendorPayment", "VoidedAt", "TEXT NULL" },
                    new[] { "VendorPayment", "VoidReason", "TEXT NULL" },
                    // D1 0040_statement_line_exclusion.sql (#436 I3)
                    new[] { "BankStatementLine", "ExcludedAt", "TEXT NULL" },
                    new[] { "BankStatementLine", "ExcludedBy", "TEXT NULL" },
                    new[] { "BankStatementLine", "ExclusionReason", "INTEGER NULL" },
                    new[] { "BankStatementLine", "ExclusionNote", "TEXT NULL" },
                    // D1 0042_statement_other_income.sql (#436 I5)
                    new[] { "SalesInvoiceHeader", "Origin", "INTEGER NULL" },
                    new[] { "Customer", "IsSystem", "INTEGER NOT NULL DEFAULT 0" }
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
                if (added.Contains("RefreshTokens.FamilyId"))
                {
                    BackfillRefreshTokenFamilyIds(connection, tx);
                }

                if (added.Contains("PurchaseInvoiceHeader.PayableAccountId"))
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            UPDATE ""PurchaseInvoiceHeader""
                            SET ""PayableAccountId"" = (
                                SELECT l.""AccountId""
                                FROM ""GeneralLedgerLine"" AS l
                                INNER JOIN ""Account"" AS a ON a.""Id"" = l.""AccountId""
                                WHERE l.""GeneralLedgerHeaderId"" = ""PurchaseInvoiceHeader"".""GeneralLedgerHeaderId""
                                  AND l.""DrCr"" = 2
                                  AND a.""AccountClassId"" = 2
                                ORDER BY l.""Amount"" DESC
                                LIMIT 1
                            )
                            WHERE ""GeneralLedgerHeaderId"" IS NOT NULL
                              AND ""PayableAccountId"" IS NULL;";
                        cmd.ExecuteNonQuery();
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
                if (added.Contains("Customer.BillToContactId") || added.Contains("Vendor.BillToContactId"))
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        CREATE INDEX IF NOT EXISTS ""IX_Customer_BillToContactId"" ON ""Customer"" (""BillToContactId"");
                        CREATE INDEX IF NOT EXISTS ""IX_Vendor_BillToContactId"" ON ""Vendor"" (""BillToContactId"");";
                    cmd.ExecuteNonQuery();
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
                if (added.Contains("GeneralLedgerHeader.SourceDocumentId") || added.Contains("GeneralLedgerHeader.ReversedGeneralLedgerHeaderId"))
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = CashBasisTraceabilityBackfillSql;
                    cmd.ExecuteNonQuery();
                }
                if (added.Contains("CompanySetting.BooksStartDate"))
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = CompanyBooksStartDateBackfillSql;
                    cmd.ExecuteNonQuery();
                }
                // E1b: backfill whenever the opt-out column exists and legacy ActiveModulesJson is still authoritative.
                BackfillDisabledModules(connection, tx);
                NormalizeCompanyPlanKeysAndTier(connection, tx);

                // #413 / D1 0032: bank columns → clean-up → indexes/triggers (idempotent).
                if (TableExists(connection, "Bank", tx) && ColumnExists(connection, "Bank", "IsDefaultForReceipts", tx))
                {
                    BankAccountCleanup.Apply(connection, tx, logger: null);
                    BankAccountCleanup.EnsureIndexesAndTriggers(connection, tx);
                }

                tx.Commit();
            }
        }

        /// <summary>
        /// Idempotent: WHERE DisabledModulesJson IS NULL AND ActiveModulesJson IS NOT NULL.
        /// Same predicates as D1 0030_entitlements_e1b.sql (self-host never runs D1).
        /// </summary>
        public static void BackfillDisabledModules(DbConnection connection, DbTransaction? tx = null)
        {
            foreach (var sql in DisabledModulesBackfillSql)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// One-statement FamilyId backfill (D1 0031). Safe under SQLITE_LIMIT_COMPOUND_SELECT=1.
        /// </summary>
        public static void BackfillRefreshTokenFamilyIds(DbConnection connection, DbTransaction? tx = null)
        {
            if (!TableExists(connection, "RefreshTokens", tx) || !ColumnExists(connection, "RefreshTokens", "FamilyId", tx))
                return;

            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = RefreshTokenFamilyBackfillSql;
            cmd.ExecuteNonQuery();

            using var indexCmd = connection.CreateCommand();
            indexCmd.Transaction = tx;
            indexCmd.CommandText =
                @"CREATE INDEX IF NOT EXISTS ""IX_RefreshTokens_FamilyId"" ON ""RefreshTokens"" (""FamilyId"");";
            indexCmd.ExecuteNonQuery();
        }

        private const string RefreshTokenFamilyBackfillSql =
            @"UPDATE ""RefreshTokens"" SET ""FamilyId"" = CAST(""Id"" AS TEXT) WHERE ""FamilyId"" IS NULL OR ""FamilyId"" = '';";

        public static void NormalizeCompanyPlanKeysAndTier(DbConnection connection, DbTransaction? tx = null)
        {
            foreach (var sql in new[]
            {
                @"UPDATE ""Company"" SET ""Plan"" = 'invoicing_free' WHERE lower(""Plan"") = 'invoicing';",
                @"UPDATE ""Company"" SET ""Plan"" = 'accounting_free' WHERE lower(""Plan"") = 'accounting';",
                @"UPDATE ""Company"" SET ""Tier"" = NULL WHERE ""Tier"" IS NOT NULL AND lower(""Tier"") != 'comp';"
            })
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

        private static readonly string[] DisabledModulesBackfillSql =
        {
            @"UPDATE ""Company""
SET ""DisabledModulesJson"" = (
  SELECT COALESCE(json_group_array(m.value), '[]')
  FROM json_each('[""sales"",""financials"",""purchases"",""reports""]') AS m
  WHERE lower(m.value) != 'sales'
    AND NOT EXISTS (
      SELECT 1 FROM json_each(""Company"".""ActiveModulesJson"") AS a
      WHERE lower(a.value) = lower(m.value)
    )
)
WHERE ""DisabledModulesJson"" IS NULL
  AND ""ActiveModulesJson"" IS NOT NULL
  AND json_valid(""ActiveModulesJson"")
  AND json_array_length(""ActiveModulesJson"") > 0
  AND lower(COALESCE(""Plan"", '')) IN ('accounting', 'accounting_free', 'accounting_pro');",
            @"UPDATE ""Company""
SET ""DisabledModulesJson"" = '[]'
WHERE ""DisabledModulesJson"" IS NULL
  AND ""ActiveModulesJson"" IS NOT NULL
  AND json_valid(""ActiveModulesJson"")
  AND json_array_length(""ActiveModulesJson"") > 0
  AND lower(COALESCE(""Plan"", '')) IN ('invoicing', 'invoicing_free', 'invoicing_pro');",
            @"UPDATE ""Company""
SET ""DisabledModulesJson"" = (
  SELECT COALESCE(json_group_array(m.value), '[]')
  FROM json_each('[""sales"",""financials"",""purchases"",""reports"",""inventory"",""donations"",""audit""]') AS m
  WHERE lower(m.value) != 'sales'
    AND NOT EXISTS (
      SELECT 1 FROM json_each(""Company"".""ActiveModulesJson"") AS a
      WHERE lower(a.value) = lower(m.value)
    )
)
WHERE ""DisabledModulesJson"" IS NULL
  AND ""ActiveModulesJson"" IS NOT NULL
  AND json_valid(""ActiveModulesJson"")
  AND json_array_length(""ActiveModulesJson"") > 0
  AND lower(COALESCE(""Plan"", '')) = 'enterprise';",
            @"UPDATE ""Company""
SET ""DisabledModulesJson"" = (
  SELECT COALESCE(json_group_array(m.value), '[]')
  FROM json_each('[""sales"",""financials"",""purchases"",""reports""]') AS m
  WHERE lower(m.value) != 'sales'
    AND NOT EXISTS (
      SELECT 1 FROM json_each(""Company"".""ActiveModulesJson"") AS a
      WHERE lower(a.value) = lower(m.value)
    )
)
WHERE ""DisabledModulesJson"" IS NULL
  AND ""ActiveModulesJson"" IS NOT NULL
  AND json_valid(""ActiveModulesJson"")
  AND json_array_length(""ActiveModulesJson"") > 0
  AND lower(COALESCE(""Plan"", '')) NOT IN (
    'accounting', 'accounting_free', 'accounting_pro',
    'invoicing', 'invoicing_free', 'invoicing_pro',
    'enterprise'
  );",
            // Empty array or invalid JSON: legacy GetActiveModules treated these as full plan.
            @"UPDATE ""Company""
SET ""DisabledModulesJson"" = '[]'
WHERE ""DisabledModulesJson"" IS NULL
  AND ""ActiveModulesJson"" IS NOT NULL;"
        };

        private const string CompanyBooksStartDateBackfillSql = @"
            UPDATE ""CompanySetting""
            SET ""BooksStartDate"" = COALESCE(
                (SELECT date(f.""StartDate"")
                 FROM ""FinancialYear"" f
                 WHERE f.""CompanyId"" = ""CompanySetting"".""CompanyId""
                 ORDER BY f.""IsActive"" DESC, f.""StartDate""
                 LIMIT 1),
                (SELECT date(c.""CreatedAt"")
                 FROM ""Company"" c
                 WHERE c.""Id"" = ""CompanySetting"".""CompanyId""),
                '1970-01-01');";

        private const string CashBasisTraceabilityBackfillSql = @"
            UPDATE ""GeneralLedgerHeader"" SET ""SourceDocumentId"" = (
                SELECT i.""Id"" FROM ""SalesInvoiceHeader"" i WHERE i.""GeneralLedgerHeaderId"" = ""GeneralLedgerHeader"".""Id"")
            WHERE ""SourceDocumentId"" IS NULL
              AND EXISTS (SELECT 1 FROM ""SalesInvoiceHeader"" i WHERE i.""GeneralLedgerHeaderId"" = ""GeneralLedgerHeader"".""Id"");
            UPDATE ""GeneralLedgerHeader"" SET
                ""SourceDocumentId"" = (SELECT i.""Id"" FROM ""SalesInvoiceHeader"" i WHERE i.""VoidGeneralLedgerHeaderId"" = ""GeneralLedgerHeader"".""Id""),
                ""ReversedGeneralLedgerHeaderId"" = (SELECT i.""GeneralLedgerHeaderId"" FROM ""SalesInvoiceHeader"" i WHERE i.""VoidGeneralLedgerHeaderId"" = ""GeneralLedgerHeader"".""Id"")
            WHERE (""SourceDocumentId"" IS NULL OR ""ReversedGeneralLedgerHeaderId"" IS NULL)
              AND EXISTS (SELECT 1 FROM ""SalesInvoiceHeader"" i WHERE i.""VoidGeneralLedgerHeaderId"" = ""GeneralLedgerHeader"".""Id"");
            UPDATE ""GeneralLedgerHeader"" SET ""SourceDocumentId"" = (
                SELECT r.""Id"" FROM ""SalesReceiptHeader"" r WHERE r.""GeneralLedgerHeaderId"" = ""GeneralLedgerHeader"".""Id"")
            WHERE ""SourceDocumentId"" IS NULL
              AND EXISTS (SELECT 1 FROM ""SalesReceiptHeader"" r WHERE r.""GeneralLedgerHeaderId"" = ""GeneralLedgerHeader"".""Id"");
            UPDATE ""GeneralLedgerHeader"" SET ""SourceDocumentId"" = (
                SELECT b.""Id"" FROM ""PurchaseInvoiceHeader"" b WHERE b.""GeneralLedgerHeaderId"" = ""GeneralLedgerHeader"".""Id"")
            WHERE ""SourceDocumentId"" IS NULL
              AND EXISTS (SELECT 1 FROM ""PurchaseInvoiceHeader"" b WHERE b.""GeneralLedgerHeaderId"" = ""GeneralLedgerHeader"".""Id"");
            UPDATE ""GeneralLedgerHeader"" SET
                ""SourceDocumentId"" = (SELECT b.""Id"" FROM ""PurchaseInvoiceHeader"" b WHERE b.""VoidGeneralLedgerHeaderId"" = ""GeneralLedgerHeader"".""Id""),
                ""ReversedGeneralLedgerHeaderId"" = (SELECT b.""GeneralLedgerHeaderId"" FROM ""PurchaseInvoiceHeader"" b WHERE b.""VoidGeneralLedgerHeaderId"" = ""GeneralLedgerHeader"".""Id"")
            WHERE (""SourceDocumentId"" IS NULL OR ""ReversedGeneralLedgerHeaderId"" IS NULL)
              AND EXISTS (SELECT 1 FROM ""PurchaseInvoiceHeader"" b WHERE b.""VoidGeneralLedgerHeaderId"" = ""GeneralLedgerHeader"".""Id"");
            UPDATE ""GeneralLedgerHeader"" SET ""SourceDocumentId"" = (
                SELECT p.""PurchaseInvoiceHeaderId"" FROM ""VendorPayment"" p WHERE p.""GeneralLedgerHeaderId"" = ""GeneralLedgerHeader"".""Id"")
            WHERE ""SourceDocumentId"" IS NULL
              AND EXISTS (SELECT 1 FROM ""VendorPayment"" p WHERE p.""GeneralLedgerHeaderId"" = ""GeneralLedgerHeader"".""Id"" AND p.""PurchaseInvoiceHeaderId"" IS NOT NULL);
            UPDATE ""GeneralLedgerHeader"" SET ""SourceDocumentId"" = (
                SELECT j.""Id"" FROM ""JournalEntryHeader"" j WHERE j.""GeneralLedgerHeaderId"" = ""GeneralLedgerHeader"".""Id"")
            WHERE ""SourceDocumentId"" IS NULL
              AND EXISTS (SELECT 1 FROM ""JournalEntryHeader"" j WHERE j.""GeneralLedgerHeaderId"" = ""GeneralLedgerHeader"".""Id"");
            UPDATE ""GeneralLedgerHeader"" SET ""SourceDocumentId"" = (
                SELECT a.""Id"" FROM ""CustomerAllocation"" a WHERE a.""GeneralLedgerHeaderId"" = ""GeneralLedgerHeader"".""Id"")
            WHERE ""SourceDocumentId"" IS NULL
              AND EXISTS (SELECT 1 FROM ""CustomerAllocation"" a WHERE a.""GeneralLedgerHeaderId"" = ""GeneralLedgerHeader"".""Id"");";

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
            cmd.CommandText = EntitlementUsageTableSql;
            cmd.ExecuteNonQuery();
            cmd.CommandText = BankTransactionTableSql;
            cmd.ExecuteNonQuery();
            BackfillOpeningBalanceBankTransactions(connection);
            EnsureBankReconciliationSchema(connection);
            EnsureBankStatementSchema(connection);
            EnsureBankStatementDescriptionRules(connection);
            EnsureBankStatementColumnMappings(connection);
            EnsureStatementOtherIncomeColumns(connection);
        }

        // D1 0043_bank_statement_column_mappings.sql (#447 Phase B)
        private static void EnsureBankStatementColumnMappings(DbConnection connection)
        {
            if (!TableExists(connection, "Bank"))
                return;

            using var cmd = connection.CreateCommand();
            cmd.CommandText = BankStatementColumnMappingSql;
            cmd.ExecuteNonQuery();
        }

        // D1 0042_statement_other_income.sql (#436 I5)
        private static void EnsureStatementOtherIncomeColumns(DbConnection connection)
        {
            AddColumnIfMissing(connection, "SalesInvoiceHeader", "Origin", "INTEGER NULL");
            AddColumnIfMissing(connection, "Customer", "IsSystem", "INTEGER NOT NULL DEFAULT 0");
            if (TableExists(connection, "SalesInvoiceHeader"))
            {
                using var idx = connection.CreateCommand();
                idx.CommandText = @"CREATE INDEX IF NOT EXISTS ""IX_SalesInvoiceHeader_Company_Origin""
                    ON ""SalesInvoiceHeader"" (""CompanyId"", ""Origin"");";
                idx.ExecuteNonQuery();
            }
        }

        // D1 0039_bank_statement_lines.sql (#413 Phase 3A) + 0040 exclusion columns (#436 I3)
        private static void EnsureBankStatementSchema(DbConnection connection)
        {
            if (!TableExists(connection, "Bank") || !TableExists(connection, "GeneralLedgerLine"))
                return;

            using var cmd = connection.CreateCommand();
            cmd.CommandText = BankStatementTablesSql;
            cmd.ExecuteNonQuery();

            EnsureBankStatementExclusionColumns(connection);
        }

        // D1 0041_statement_description_rules.sql (#436 I4)
        private static void EnsureBankStatementDescriptionRules(DbConnection connection)
        {
            if (!TableExists(connection, "Vendor") || !TableExists(connection, "Account"))
                return;

            using var cmd = connection.CreateCommand();
            cmd.CommandText = BankStatementDescriptionRuleSql;
            cmd.ExecuteNonQuery();
        }

        private static void EnsureBankStatementExclusionColumns(DbConnection connection)
        {
            AddColumnIfMissing(connection, "BankStatementLine", "ExcludedAt", "TEXT NULL");
            AddColumnIfMissing(connection, "BankStatementLine", "ExcludedBy", "TEXT NULL");
            AddColumnIfMissing(connection, "BankStatementLine", "ExclusionReason", "INTEGER NULL");
            AddColumnIfMissing(connection, "BankStatementLine", "ExclusionNote", "TEXT NULL");

            using var idx = connection.CreateCommand();
            idx.CommandText = @"CREATE INDEX IF NOT EXISTS ""IX_BankStatementLine_Company_Bank_ExcludedAt""
                ON ""BankStatementLine"" (""CompanyId"", ""BankId"", ""ExcludedAt"");";
            idx.ExecuteNonQuery();
        }

        private static void AddColumnIfMissing(DbConnection connection, string table, string column, string sqlType)
        {
            if (!TableExists(connection, table) || ColumnExists(connection, table, column))
                return;
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {sqlType};";
            cmd.ExecuteNonQuery();
        }

        // D1 0037_bank_reconciliation.sql (#413 Phase 2B). Plan called this part of 0034; 0035/0036 shipped first.
        private static void EnsureBankReconciliationSchema(DbConnection connection)
        {
            if (!TableExists(connection, "Bank") || !TableExists(connection, "GeneralLedgerLine"))
                return;

            using var cmd = connection.CreateCommand();
            cmd.CommandText = BankReconciliationTableSql;
            cmd.ExecuteNonQuery();
            cmd.CommandText = BankReconciliationItemTableSql;
            cmd.ExecuteNonQuery();

            if (!ColumnExists(connection, "GeneralLedgerLine", "BankReconciliationId"))
            {
                cmd.CommandText = @"ALTER TABLE ""GeneralLedgerLine"" ADD COLUMN ""BankReconciliationId"" INTEGER NULL REFERENCES ""BankReconciliation"" (""Id"");";
                cmd.ExecuteNonQuery();
            }

            cmd.CommandText = @"CREATE INDEX IF NOT EXISTS ""IX_GeneralLedgerLine_Company_BankReconciliationId"" ON ""GeneralLedgerLine"" (""CompanyId"", ""BankReconciliationId"");";
            cmd.ExecuteNonQuery();

            // D1 0038_bank_reconciliation_report_cutoff.sql (#413 Phase 2D)
            if (!ColumnExists(connection, "BankReconciliation", "FinishedMaxGeneralLedgerHeaderId"))
            {
                cmd.CommandText = @"ALTER TABLE ""BankReconciliation"" ADD COLUMN ""FinishedMaxGeneralLedgerHeaderId"" INTEGER NULL;";
                cmd.ExecuteNonQuery();
            }
        }

        // D1 0026_security_schema.sql
        private static void EnsureSecurityTables(DbConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = SecurityTablesSql;
            cmd.ExecuteNonQuery();
        }

        private const string SecurityTablesSql = @"
            CREATE TABLE IF NOT EXISTS ""RefreshTokens"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_RefreshTokens"" PRIMARY KEY AUTOINCREMENT,
                ""UserId"" TEXT NOT NULL,
                ""FamilyId"" TEXT NOT NULL DEFAULT '',
                ""TokenHash"" TEXT NOT NULL,
                ""ExpiresAt"" TEXT NOT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""CreatedByIp"" TEXT NULL,
                ""RevokedAt"" TEXT NULL,
                ""ReplacedByTokenHash"" TEXT NULL,
                ""UserAgent"" TEXT NULL,
                ""GraceReuseCount"" INTEGER NOT NULL DEFAULT 0,
                ""MfaMethod"" TEXT NULL,
                ""Amr"" TEXT NULL,
                ""TrustedDeviceId"" INTEGER NULL
            );
            CREATE INDEX IF NOT EXISTS ""IX_RefreshTokens_TokenHash"" ON ""RefreshTokens"" (""TokenHash"");
            CREATE INDEX IF NOT EXISTS ""IX_RefreshTokens_UserId"" ON ""RefreshTokens"" (""UserId"");
            CREATE INDEX IF NOT EXISTS ""IX_RefreshTokens_FamilyId"" ON ""RefreshTokens"" (""FamilyId"");

            CREATE TABLE IF NOT EXISTS ""DataProtectionKeys"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_DataProtectionKeys"" PRIMARY KEY AUTOINCREMENT,
                ""FriendlyName"" TEXT NOT NULL,
                ""Xml"" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ""IX_DataProtectionKeys_FriendlyName"" ON ""DataProtectionKeys"" (""FriendlyName"");

            CREATE TABLE IF NOT EXISTS ""SecurityAuditEvents"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_SecurityAuditEvents"" PRIMARY KEY AUTOINCREMENT,
                ""UserId"" TEXT NULL,
                ""CompanyId"" INTEGER NULL,
                ""EventType"" TEXT NOT NULL,
                ""IpAddress"" TEXT NULL,
                ""UserAgent"" TEXT NULL,
                ""MetadataJson"" TEXT NULL,
                ""CreatedAt"" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ""IX_SecurityAuditEvents_EventType"" ON ""SecurityAuditEvents"" (""EventType"");
            CREATE INDEX IF NOT EXISTS ""IX_SecurityAuditEvents_UserId_CreatedAt"" ON ""SecurityAuditEvents"" (""UserId"", ""CreatedAt"");

            CREATE TABLE IF NOT EXISTS ""UserRecoveryCodes"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_UserRecoveryCodes"" PRIMARY KEY AUTOINCREMENT,
                ""UserId"" TEXT NOT NULL,
                ""CodeHash"" TEXT NOT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""ConsumedAt"" TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ""IX_UserRecoveryCodes_UserId"" ON ""UserRecoveryCodes"" (""UserId"");
            CREATE INDEX IF NOT EXISTS ""IX_UserRecoveryCodes_CodeHash"" ON ""UserRecoveryCodes"" (""CodeHash"");

            CREATE TABLE IF NOT EXISTS ""TrustedDevices"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_TrustedDevices"" PRIMARY KEY AUTOINCREMENT,
                ""UserId"" TEXT NOT NULL,
                ""TokenHash"" TEXT NOT NULL,
                ""Label"" TEXT NULL,
                ""UserAgent"" TEXT NULL,
                ""CreatedByIp"" TEXT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""ExpiresAt"" TEXT NOT NULL,
                ""LastSeenAt"" TEXT NOT NULL,
                ""RevokedAt"" TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ""IX_TrustedDevices_UserId"" ON ""TrustedDevices"" (""UserId"");
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TrustedDevices_TokenHash"" ON ""TrustedDevices"" (""TokenHash"");

            CREATE TABLE IF NOT EXISTS ""UserWebAuthnCredentials"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_UserWebAuthnCredentials"" PRIMARY KEY AUTOINCREMENT,
                ""UserId"" TEXT NOT NULL,
                ""CredentialId"" BLOB NOT NULL,
                ""PublicKey"" BLOB NOT NULL,
                ""UserHandle"" BLOB NOT NULL,
                ""SignatureCounter"" INTEGER NOT NULL,
                ""CredType"" TEXT NULL,
                ""AaGuid"" TEXT NOT NULL,
                ""Nickname"" TEXT NOT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""LastUsedAt"" TEXT NULL,
                ""Transports"" TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ""IX_UserWebAuthnCredentials_UserId"" ON ""UserWebAuthnCredentials"" (""UserId"");
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_UserWebAuthnCredentials_CredentialId"" ON ""UserWebAuthnCredentials"" (""CredentialId"");

            CREATE TABLE IF NOT EXISTS ""WebAuthnChallenges"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_WebAuthnChallenges"" PRIMARY KEY AUTOINCREMENT,
                ""UserId"" TEXT NOT NULL,
                ""Purpose"" TEXT NOT NULL,
                ""JsonPayload"" TEXT NOT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""ExpiresAt"" TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_WebAuthnChallenges_UserId_Purpose"" ON ""WebAuthnChallenges"" (""UserId"", ""Purpose"");
        ";

        // D1 0024_entitlements.sql
        private const string EntitlementUsageTableSql = @"
            CREATE TABLE IF NOT EXISTS ""EntitlementUsage"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_EntitlementUsage"" PRIMARY KEY AUTOINCREMENT,
                ""CompanyId"" INTEGER NOT NULL,
                ""Resource"" TEXT NOT NULL,
                ""EntityId"" INTEGER NULL,
                ""OccurredAt"" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ""IX_EntitlementUsage_CompanyId_Resource_OccurredAt"" ON ""EntitlementUsage"" (""CompanyId"", ""Resource"", ""OccurredAt"");";

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

        // D1 0036_bank_transactions.sql (#413 Phase 2A)
        private const string BankTransactionTableSql = @"
            CREATE TABLE IF NOT EXISTS ""BankTransaction"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_BankTransaction"" PRIMARY KEY AUTOINCREMENT,
                ""CompanyId"" INTEGER NOT NULL,
                ""Kind"" INTEGER NOT NULL,
                ""BankId"" INTEGER NOT NULL,
                ""ToBankId"" INTEGER NULL,
                ""Date"" TEXT NOT NULL,
                ""Amount"" NUMERIC NOT NULL,
                ""ContraAccountId"" INTEGER NULL,
                ""PartyId"" INTEGER NULL,
                ""No"" TEXT NOT NULL,
                ""ReferenceNo"" TEXT NULL,
                ""Memo"" TEXT NULL,
                ""GeneralLedgerHeaderId"" INTEGER NULL,
                ""VoidGeneralLedgerHeaderId"" INTEGER NULL,
                CONSTRAINT ""FK_BankTransaction_Bank_BankId"" FOREIGN KEY (""BankId"") REFERENCES ""Bank"" (""Id""),
                CONSTRAINT ""FK_BankTransaction_Bank_ToBankId"" FOREIGN KEY (""ToBankId"") REFERENCES ""Bank"" (""Id""),
                CONSTRAINT ""FK_BankTransaction_Account_ContraAccountId"" FOREIGN KEY (""ContraAccountId"") REFERENCES ""Account"" (""Id""),
                CONSTRAINT ""FK_BankTransaction_Party_PartyId"" FOREIGN KEY (""PartyId"") REFERENCES ""Party"" (""Id""),
                CONSTRAINT ""FK_BankTransaction_GeneralLedgerHeader_GeneralLedgerHeaderId"" FOREIGN KEY (""GeneralLedgerHeaderId"") REFERENCES ""GeneralLedgerHeader"" (""Id""),
                CONSTRAINT ""FK_BankTransaction_GeneralLedgerHeader_VoidGeneralLedgerHeaderId"" FOREIGN KEY (""VoidGeneralLedgerHeaderId"") REFERENCES ""GeneralLedgerHeader"" (""Id"")
            );
            CREATE INDEX IF NOT EXISTS ""IX_BankTransaction_CompanyId"" ON ""BankTransaction"" (""CompanyId"");
            CREATE INDEX IF NOT EXISTS ""IX_BankTransaction_Company_Bank_Date"" ON ""BankTransaction"" (""CompanyId"", ""BankId"", ""Date"");
            CREATE INDEX IF NOT EXISTS ""IX_BankTransaction_GeneralLedgerHeaderId"" ON ""BankTransaction"" (""GeneralLedgerHeaderId"");
            CREATE UNIQUE INDEX IF NOT EXISTS ""UX_BankTransaction_Company_No"" ON ""BankTransaction"" (""CompanyId"", ""No"");";

        // D1 0037_bank_reconciliation.sql (#413 Phase 2B)
        private const string BankReconciliationTableSql = @"
            CREATE TABLE IF NOT EXISTS ""BankReconciliation"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_BankReconciliation"" PRIMARY KEY AUTOINCREMENT,
                ""CompanyId"" INTEGER NOT NULL,
                ""BankId"" INTEGER NOT NULL,
                ""StatementEndDate"" TEXT NOT NULL,
                ""StatementEndingBalance"" NUMERIC NOT NULL,
                ""BeginningBalance"" NUMERIC NOT NULL,
                ""Status"" INTEGER NOT NULL,
                ""CreatedBy"" TEXT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""FinishedBy"" TEXT NULL,
                ""FinishedAt"" TEXT NULL,
                ""ReviewedBy"" TEXT NULL,
                ""ReviewedAt"" TEXT NULL,
                ""UndoneBy"" TEXT NULL,
                ""UndoneAt"" TEXT NULL,
                ""UndoReason"" TEXT NULL,
                ""Version"" INTEGER NOT NULL DEFAULT 1,
                ""FinishedMaxGeneralLedgerHeaderId"" INTEGER NULL,
                CONSTRAINT ""FK_BankReconciliation_Bank_BankId"" FOREIGN KEY (""BankId"") REFERENCES ""Bank"" (""Id"")
            );
            CREATE INDEX IF NOT EXISTS ""IX_BankReconciliation_CompanyId"" ON ""BankReconciliation"" (""CompanyId"");
            CREATE INDEX IF NOT EXISTS ""IX_BankReconciliation_Company_Bank_EndDate"" ON ""BankReconciliation"" (""CompanyId"", ""BankId"", ""StatementEndDate"");
            CREATE UNIQUE INDEX IF NOT EXISTS ""UX_BankReconciliation_OneDraft"" ON ""BankReconciliation"" (""CompanyId"", ""BankId"") WHERE ""Status"" = 1;";

        private const string BankReconciliationItemTableSql = @"
            CREATE TABLE IF NOT EXISTS ""BankReconciliationItem"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_BankReconciliationItem"" PRIMARY KEY AUTOINCREMENT,
                ""CompanyId"" INTEGER NOT NULL,
                ""BankReconciliationId"" INTEGER NOT NULL,
                ""GeneralLedgerLineId"" INTEGER NOT NULL,
                ""Amount"" NUMERIC NOT NULL,
                ""DrCr"" INTEGER NOT NULL,
                CONSTRAINT ""FK_BankReconciliationItem_BankReconciliation_BankReconciliationId"" FOREIGN KEY (""BankReconciliationId"") REFERENCES ""BankReconciliation"" (""Id""),
                CONSTRAINT ""FK_BankReconciliationItem_GeneralLedgerLine_GeneralLedgerLineId"" FOREIGN KEY (""GeneralLedgerLineId"") REFERENCES ""GeneralLedgerLine"" (""Id"")
            );
            CREATE INDEX IF NOT EXISTS ""IX_BankReconciliationItem_CompanyId"" ON ""BankReconciliationItem"" (""CompanyId"");
            CREATE INDEX IF NOT EXISTS ""IX_BankReconciliationItem_BankReconciliationId"" ON ""BankReconciliationItem"" (""BankReconciliationId"");
            CREATE UNIQUE INDEX IF NOT EXISTS ""UX_BankReconciliationItem_Recon_Line"" ON ""BankReconciliationItem"" (""BankReconciliationId"", ""GeneralLedgerLineId"");";

        private const string BankStatementTablesSql = @"
            CREATE TABLE IF NOT EXISTS ""BankStatementImport"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_BankStatementImport"" PRIMARY KEY AUTOINCREMENT,
                ""CompanyId"" INTEGER NOT NULL,
                ""BankId"" INTEGER NOT NULL,
                ""Source"" INTEGER NOT NULL,
                ""FileName"" TEXT NULL,
                ""ImportedAt"" TEXT NOT NULL,
                ""ImportedBy"" TEXT NULL,
                ""ColumnMapJson"" TEXT NULL,
                CONSTRAINT ""FK_BankStatementImport_Bank_BankId"" FOREIGN KEY (""BankId"") REFERENCES ""Bank"" (""Id"")
            );
            CREATE INDEX IF NOT EXISTS ""IX_BankStatementImport_CompanyId"" ON ""BankStatementImport"" (""CompanyId"");
            CREATE INDEX IF NOT EXISTS ""IX_BankStatementImport_Company_Bank"" ON ""BankStatementImport"" (""CompanyId"", ""BankId"");
            CREATE INDEX IF NOT EXISTS ""IX_BankStatementImport_BankId"" ON ""BankStatementImport"" (""BankId"");
            CREATE TABLE IF NOT EXISTS ""BankStatementLine"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_BankStatementLine"" PRIMARY KEY AUTOINCREMENT,
                ""CompanyId"" INTEGER NOT NULL,
                ""BankId"" INTEGER NOT NULL,
                ""BankStatementImportId"" INTEGER NOT NULL,
                ""Source"" INTEGER NOT NULL,
                ""ExternalId"" TEXT NULL,
                ""PostedDate"" TEXT NOT NULL,
                ""Amount"" NUMERIC NOT NULL,
                ""Payee"" TEXT NULL,
                ""Memo"" TEXT NULL,
                ""Reference"" TEXT NULL,
                ""MatchedGeneralLedgerLineId"" INTEGER NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""ExcludedAt"" TEXT NULL,
                ""ExcludedBy"" TEXT NULL,
                ""ExclusionReason"" INTEGER NULL,
                ""ExclusionNote"" TEXT NULL,
                CONSTRAINT ""FK_BankStatementLine_Bank_BankId"" FOREIGN KEY (""BankId"") REFERENCES ""Bank"" (""Id""),
                CONSTRAINT ""FK_BankStatementLine_BankStatementImport_BankStatementImportId"" FOREIGN KEY (""BankStatementImportId"") REFERENCES ""BankStatementImport"" (""Id""),
                CONSTRAINT ""FK_BankStatementLine_GeneralLedgerLine_MatchedGeneralLedgerLineId"" FOREIGN KEY (""MatchedGeneralLedgerLineId"") REFERENCES ""GeneralLedgerLine"" (""Id"")
            );
            CREATE INDEX IF NOT EXISTS ""IX_BankStatementLine_CompanyId"" ON ""BankStatementLine"" (""CompanyId"");
            CREATE INDEX IF NOT EXISTS ""IX_BankStatementLine_BankStatementImportId"" ON ""BankStatementLine"" (""BankStatementImportId"");
            CREATE INDEX IF NOT EXISTS ""IX_BankStatementLine_MatchedGeneralLedgerLineId"" ON ""BankStatementLine"" (""MatchedGeneralLedgerLineId"");
            CREATE INDEX IF NOT EXISTS ""IX_BankStatementLine_BankId"" ON ""BankStatementLine"" (""BankId"");
            CREATE INDEX IF NOT EXISTS ""IX_BankStatementLine_Company_Bank_PostedDate"" ON ""BankStatementLine"" (""CompanyId"", ""BankId"", ""PostedDate"");
            CREATE INDEX IF NOT EXISTS ""IX_BankStatementLine_Company_Bank_ExcludedAt"" ON ""BankStatementLine"" (""CompanyId"", ""BankId"", ""ExcludedAt"");
            CREATE UNIQUE INDEX IF NOT EXISTS ""UX_BankStatementLine_Company_Bank_Source_External"" ON ""BankStatementLine"" (""CompanyId"", ""BankId"", ""Source"", ""ExternalId"") WHERE ""ExternalId"" IS NOT NULL;";

        private const string BankStatementDescriptionRuleSql = @"
            CREATE TABLE IF NOT EXISTS ""BankStatementDescriptionRule"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_BankStatementDescriptionRule"" PRIMARY KEY AUTOINCREMENT,
                ""CompanyId"" INTEGER NOT NULL,
                ""DescriptionKey"" TEXT NOT NULL,
                ""VendorId"" INTEGER NULL,
                ""ExpenseAccountId"" INTEGER NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""LastUsedAt"" TEXT NOT NULL,
                CONSTRAINT ""FK_BankStatementDescriptionRule_Vendor_VendorId"" FOREIGN KEY (""VendorId"") REFERENCES ""Vendor"" (""Id""),
                CONSTRAINT ""FK_BankStatementDescriptionRule_Account_ExpenseAccountId"" FOREIGN KEY (""ExpenseAccountId"") REFERENCES ""Account"" (""Id"")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ""UX_BankStatementDescriptionRule_Company_DescriptionKey""
                ON ""BankStatementDescriptionRule"" (""CompanyId"", ""DescriptionKey"");
            CREATE INDEX IF NOT EXISTS ""IX_BankStatementDescriptionRule_CompanyId"" ON ""BankStatementDescriptionRule"" (""CompanyId"");
            CREATE INDEX IF NOT EXISTS ""IX_BankStatementDescriptionRule_VendorId"" ON ""BankStatementDescriptionRule"" (""VendorId"");
            CREATE INDEX IF NOT EXISTS ""IX_BankStatementDescriptionRule_ExpenseAccountId"" ON ""BankStatementDescriptionRule"" (""ExpenseAccountId"");";

        private const string BankStatementColumnMappingSql = @"
            CREATE TABLE IF NOT EXISTS ""BankStatementColumnMapping"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_BankStatementColumnMapping"" PRIMARY KEY AUTOINCREMENT,
                ""CompanyId"" INTEGER NOT NULL,
                ""BankId"" INTEGER NOT NULL,
                ""HeaderSignature"" TEXT NOT NULL,
                ""ColumnMapJson"" TEXT NOT NULL,
                ""UpdatedAt"" TEXT NOT NULL,
                CONSTRAINT ""FK_BankStatementColumnMapping_Bank_BankId"" FOREIGN KEY (""BankId"") REFERENCES ""Bank"" (""Id"")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ""UX_BankStatementColumnMapping_Company_Bank_Header""
                ON ""BankStatementColumnMapping"" (""CompanyId"", ""BankId"", ""HeaderSignature"");
            CREATE INDEX IF NOT EXISTS ""IX_BankStatementColumnMapping_CompanyId"" ON ""BankStatementColumnMapping"" (""CompanyId"");
            CREATE INDEX IF NOT EXISTS ""IX_BankStatementColumnMapping_BankId"" ON ""BankStatementColumnMapping"" (""BankId"");";

        private static void BackfillOpeningBalanceBankTransactions(DbConnection connection)
        {
            if (!TableExists(connection, "BankTransaction") || !TableExists(connection, "Bank") || !TableExists(connection, "GeneralLedgerHeader"))
                return;

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE ""GeneralLedgerHeader""
                SET ""SourceDocumentId"" = (
                    SELECT b.""Id"" FROM ""Bank"" AS b
                    WHERE b.""OpeningBalanceGeneralLedgerHeaderId"" = ""GeneralLedgerHeader"".""Id""
                      AND b.""CompanyId"" = ""GeneralLedgerHeader"".""CompanyId"")
                WHERE ""DocumentType"" = 17
                  AND ""Description"" = 'Opening balance'
                  AND ""SourceDocumentId"" IS NULL
                  AND EXISTS (
                    SELECT 1 FROM ""Bank"" AS b
                    WHERE b.""OpeningBalanceGeneralLedgerHeaderId"" = ""GeneralLedgerHeader"".""Id""
                      AND b.""CompanyId"" = ""GeneralLedgerHeader"".""CompanyId"");

                INSERT INTO ""BankTransaction"" (
                    ""CompanyId"", ""Kind"", ""BankId"", ""ToBankId"", ""Date"", ""Amount"",
                    ""ContraAccountId"", ""PartyId"", ""No"", ""ReferenceNo"", ""Memo"",
                    ""GeneralLedgerHeaderId"", ""VoidGeneralLedgerHeaderId"")
                SELECT b.""CompanyId"", 7, b.""Id"", NULL,
                       COALESCE(b.""OpeningBalanceDate"", date('now')),
                       ABS(COALESCE(b.""OpeningBalance"", 0)),
                       NULL, NULL, 'OB-' || CAST(b.""Id"" AS TEXT), NULL, 'Opening balance',
                       b.""OpeningBalanceGeneralLedgerHeaderId"", NULL
                FROM ""Bank"" AS b
                WHERE b.""OpeningBalanceGeneralLedgerHeaderId"" IS NOT NULL
                  AND COALESCE(b.""OpeningBalance"", 0) != 0
                  AND NOT EXISTS (
                    SELECT 1 FROM ""BankTransaction"" AS t
                    WHERE t.""BankId"" = b.""Id"" AND t.""Kind"" = 7 AND t.""VoidGeneralLedgerHeaderId"" IS NULL);";
            cmd.ExecuteNonQuery();
        }

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
            if (TableExists(connection, "Bank") && ColumnExists(connection, "Bank", "IsDefaultForReceipts"))
            {
                BankAccountCleanup.EnsureIndexesAndTriggers(connection);
            }

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

            // D1: support@accountgo.dev = Admin (2)
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO ""CompanyMember"" (""CompanyId"", ""UserId"", ""Role"", ""Status"", ""JoinedAt"")
                    SELECT 1, ""Id"", 2, 1, $now
                    FROM ""AspNetUsers""
                    WHERE LOWER(""Email"") = 'support@accountgo.dev'
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
                    logger?.LogInformation("✓ Backfilled Admin membership for support@accountgo.dev in Company 1.");
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
