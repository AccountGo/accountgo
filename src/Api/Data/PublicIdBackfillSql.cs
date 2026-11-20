// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Data
{
    public enum PublicIdSqlDialect
    {
        Sqlite,
        Postgres,
        SqlServer
    }

    public sealed record PublicIdTableSpec(string TableName, string Prefix, bool IsIdentityTable);

    /// <summary>
    /// Single source of truth for generating idempotent PublicId backfill SQL across SQLite, PostgreSQL, and SQL Server.
    /// Used by EF migrations, <see cref="Schema.SqliteSchemaUpgrader"/>, and <c>dotnet Api.dll --backfill-public-ids</c>.
    /// </summary>
    public static class PublicIdBackfillSql
    {
        public static PublicIdSqlDialect DialectForProvider(string? providerName)
        {
            if (string.IsNullOrWhiteSpace(providerName))
                return PublicIdSqlDialect.Sqlite;
            if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
                || providerName.Contains("Postgres", StringComparison.OrdinalIgnoreCase))
                return PublicIdSqlDialect.Postgres;
            if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
                return PublicIdSqlDialect.SqlServer;
            return PublicIdSqlDialect.Sqlite;
        }

        public static IReadOnlyList<PublicIdTableSpec> GetTableSpecs(DbContext context, bool isIdentity)
        {
            var result = new List<PublicIdTableSpec>();
            foreach (var entityType in context.Model.GetEntityTypes())
            {
                if (!typeof(IHasPublicId).IsAssignableFrom(entityType.ClrType))
                    continue;
                var attr = entityType.ClrType.GetCustomAttribute<PublicIdPrefixAttribute>(inherit: false);
                if (attr == null)
                    continue;
                var tableName = entityType.GetTableName();
                if (string.IsNullOrWhiteSpace(tableName))
                    continue;
                result.Add(new PublicIdTableSpec(tableName, attr.Prefix, isIdentity));
            }
            return result
                .GroupBy(s => s.TableName, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(s => s.TableName, StringComparer.Ordinal)
                .ToList();
        }

        public static string UpdateStatement(PublicIdSqlDialect dialect, string tableName, string prefix) => dialect switch
        {
            PublicIdSqlDialect.Sqlite =>
                $"UPDATE \"{tableName}\" SET \"PublicId\" = '{prefix}_' || lower(hex(randomblob(13))) WHERE \"PublicId\" = '';",
            PublicIdSqlDialect.Postgres =>
                $"UPDATE \"{tableName}\" SET \"PublicId\" = '{prefix}_' || substr(replace(gen_random_uuid()::text,'-',''),1,26) WHERE \"PublicId\" = '';",
            PublicIdSqlDialect.SqlServer =>
                $"UPDATE [{tableName}] SET [PublicId] = '{prefix}_' + LOWER(LEFT(REPLACE(CAST(NEWID() AS varchar(36)), '-', ''), 26)) WHERE [PublicId] = '';",
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, null)
        };

        public static IReadOnlyList<string> For(string? providerName, IEnumerable<PublicIdTableSpec> tables)
        {
            var dialect = DialectForProvider(providerName);
            return tables.Select(t => UpdateStatement(dialect, t.TableName, t.Prefix)).ToList();
        }

        /// <summary>
        /// Static table-to-prefix list for migrations so migrations do not depend on runtime model changes.
        /// </summary>
        public static readonly IReadOnlyList<PublicIdTableSpec> ApiTables = new PublicIdTableSpec[]
        {
            new("Account", "acct", false),
            new("AuditLog", "alog", false),
            new("Bank", "ba", false),
            new("BankReconciliation", "rec", false),
            new("BankReconciliationItem", "reci", false),
            new("BankStatementDescriptionRule", "bsdr", false),
            new("BankStatementImport", "bsi", false),
            new("BankStatementLine", "bsl", false),
            new("BankTransaction", "btx", false),
            new("Company", "ws", false),
            new("CompanyInvitation", "winv", false),
            new("CompanyMember", "mem", false),
            new("Contact", "cont", false),
            new("Customer", "cus", false),
            new("CustomerAllocation", "alloc", false),
            new("CustomerCreditNote", "cn", false),
            new("CustomerCreditNoteLine", "cnl", false),
            new("CustomerReturnReceipt", "crr", false),
            new("CustomerReturnReceiptLine", "crrl", false),
            new("DonationInvoiceHeader", "dinv", false),
            new("DonationInvoiceLine", "dil", false),
            new("Donors", "dnr", false),
            new("FinancialYear", "fy", false),
            new("GeneralLedgerHeader", "gl", false),
            new("GeneralLedgerLine", "gll", false),
            new("InventoryAdjustment", "iadj", false),
            new("InventoryControlJournal", "icj", false),
            new("Item", "item", false),
            new("ItemCategory", "icat", false),
            new("ItemTaxGroup", "itg", false),
            new("JournalEntryHeader", "je", false),
            new("JournalEntryLine", "jel", false),
            new("LinkedEntryPairs", "lep", false),
            new("Measurement", "uom", false),
            new("Partners", "ptnr", false),
            new("Party", "pty", false),
            new("PartyRefund", "rf", false),
            new("PartyRefundAllocation", "rfa", false),
            new("PaymentTerm", "term", false),
            new("PurchaseInvoiceHeader", "bill", false),
            new("PurchaseInvoiceLine", "bl", false),
            new("PurchaseOrderHeader", "po", false),
            new("PurchaseOrderLine", "pol", false),
            new("PurchaseReceiptHeader", "prc", false),
            new("PurchaseReceiptLine", "prcl", false),
            new("ReceivableWriteOff", "wo", false),
            new("RecurringOccurrences", "rocc", false),
            new("RecurringScheduleLines", "rschl", false),
            new("RecurringSchedules", "rsch", false),
            new("SalesDeliveryHeader", "del", false),
            new("SalesInvoiceEmail", "iem", false),
            new("SalesInvoiceHeader", "inv", false),
            new("SalesInvoiceLine", "il", false),
            new("SalesOrderHeader", "so", false),
            new("SalesOrderLine", "sol", false),
            new("SalesQuoteHeader", "quo", false),
            new("SalesQuoteLine", "quol", false),
            new("SalesReceiptHeader", "rcpt", false),
            new("SalesReceiptLine", "rcptl", false),
            new("SecurityRole", "role", false),
            new("SettlementApplication", "sapp", false),
            new("SupplierAdvance", "adv", false),
            new("SupplierCreditNote", "scn", false),
            new("SupplierCreditNoteLine", "scnl", false),
            new("SupplierReturnDispatch", "srd", false),
            new("SupplierReturnDispatchLine", "srdl", false),
            new("Tax", "tax", false),
            new("TaxFiling", "txf", false),
            new("TaxGroup", "txg", false),
            new("Vendor", "ven", false),
            new("VendorPayment", "vpay", false),
            new("WriteOffRecovery", "wor", false),
            new("YearEndCloseRun", "yecr", false),
        };

        public static readonly IReadOnlyList<PublicIdTableSpec> IdentityTables = new PublicIdTableSpec[]
        {
            new("RefreshTokens", "sess", true),
            new("TrustedDevices", "dev", true),
            new("UserWebAuthnCredentials", "pkey", true),
        };

        public static IReadOnlyList<string> ForApi(string? providerName) => For(providerName, ApiTables);
        public static IReadOnlyList<string> ForIdentity(string? providerName) => For(providerName, IdentityTables);

        /// <summary>
        /// Idempotent repair run invoked by <c>dotnet Api.dll --backfill-public-ids</c> (and tests).
        /// Returns total number of rows repaired across ApiDbContext and ApplicationIdentityDbContext.
        /// </summary>
        public static int RepairAll(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var apiDb = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var identityDb = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();

            var total = 0;
            total += RepairContext(apiDb, isIdentity: false);
            total += RepairContext(identityDb, isIdentity: true);
            return total;
        }

        public static int RepairContext(DbContext context, bool isIdentity)
        {
            var dialect = DialectForProvider(context.Database.ProviderName);
            var specs = GetTableSpecs(context, isIdentity);
            var total = 0;
            foreach (var spec in specs)
            {
                var sql = UpdateStatement(dialect, spec.TableName, spec.Prefix);
                total += context.Database.ExecuteSqlRaw(sql);
            }
            return total;
        }

        public static int RepairSqliteConnection(DbConnection connection, DbTransaction? tx = null)
        {
            var total = 0;
            foreach (var spec in ApiTables.Concat(IdentityTables))
            {
                if (!SqliteTableAndColumnExist(connection, spec.TableName, "PublicId", tx))
                    continue;
                using var cmd = connection.CreateCommand();
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandText = UpdateStatement(PublicIdSqlDialect.Sqlite, spec.TableName, spec.Prefix);
                total += cmd.ExecuteNonQuery();
            }
            return total;
        }

        private static bool SqliteTableAndColumnExist(DbConnection connection, string tableName, string columnName, DbTransaction? tx)
        {
            using (var check = connection.CreateCommand())
            {
                if (tx != null) check.Transaction = tx;
                check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$t;";
                var p = check.CreateParameter();
                p.ParameterName = "$t";
                p.Value = tableName;
                check.Parameters.Add(p);
                if (Convert.ToInt32(check.ExecuteScalar()) == 0)
                    return false;
            }
            using (var pragma = connection.CreateCommand())
            {
                if (tx != null) pragma.Transaction = tx;
                pragma.CommandText = $"PRAGMA table_info(\"{tableName}\");";
                using var reader = pragma.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }
    }
}
