// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Services.Banking;

namespace Api.Data.Schema
{
    /// <summary>
    /// C# copy of the #413 / 0032 bank clean-up for local and container SQLite databases.
    /// D1 cannot loop to allocate dedicated cash accounts; production uses
    /// <c>scripts/d1/bank-preflight.sql</c> plus a reviewed one-off fix first.
    /// </summary>
    public static class BankAccountCleanup
    {
        public static void Apply(DbConnection connection, DbTransaction? tx = null, ILogger? logger = null)
        {
            if (!TableExists(connection, "Bank", tx))
                return;

            BackfillDefaultsAndArchive(connection, tx);
            FillBlankNames(connection, tx);
            DeduplicateNames(connection, tx);
            CollapseMultipleDefaults(connection, tx);
            RepairInvalidAndSharedAccounts(connection, tx, logger);
            EnsureNoNullAccountIds(connection, tx);
        }

        public static void EnsureIndexesAndTriggers(DbConnection connection, DbTransaction? tx = null)
        {
            if (!TableExists(connection, "Bank", tx) || !ColumnExists(connection, "Bank", "IsDefaultForReceipts", tx))
                return;

            foreach (var sql in new[]
            {
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""UX_Bank_Company_Account"" ON ""Bank"" (""CompanyId"", ""AccountId"");",
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""UX_Bank_Company_Name"" ON ""Bank"" (""CompanyId"", ""Name"" COLLATE NOCASE);",
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""UX_Bank_DefaultReceipts"" ON ""Bank"" (""CompanyId"") WHERE ""IsDefaultForReceipts"" = 1;",
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""UX_Bank_DefaultPayments"" ON ""Bank"" (""CompanyId"") WHERE ""IsDefaultForPayments"" = 1;",
                @"DROP TRIGGER IF EXISTS ""trg_Bank_AccountId_required_insert"";",
                @"CREATE TRIGGER ""trg_Bank_AccountId_required_insert"" BEFORE INSERT ON ""Bank"" BEGIN SELECT RAISE(ABORT, 'Bank.AccountId is required') WHERE NEW.""AccountId"" IS NULL; END;",
                @"DROP TRIGGER IF EXISTS ""trg_Bank_AccountId_required_update"";",
                @"CREATE TRIGGER ""trg_Bank_AccountId_required_update"" BEFORE UPDATE ON ""Bank"" BEGIN SELECT RAISE(ABORT, 'Bank.AccountId is required') WHERE NEW.""AccountId"" IS NULL; END;"
            })
            {
                Execute(connection, tx, sql);
            }
        }

        private static void BackfillDefaultsAndArchive(DbConnection connection, DbTransaction? tx)
        {
            if (!ColumnExists(connection, "Bank", "IsDefaultForReceipts", tx))
                return;

            // Only promote legacy IsDefault when the company still has no receipt default
            // (idempotent: collapse may have cleared extras while IsDefault stayed 1).
            Execute(connection, tx, @"
UPDATE ""Bank"" SET ""IsDefaultForReceipts"" = 1
WHERE ""IsDefault"" = 1
  AND ""IsDefaultForReceipts"" = 0
  AND NOT EXISTS (
    SELECT 1 FROM ""Bank"" AS other
    WHERE other.""CompanyId"" = ""Bank"".""CompanyId""
      AND other.""IsDefaultForReceipts"" = 1
  );");
            Execute(connection, tx,
                @"UPDATE ""Bank"" SET ""ArchivedAt"" = CURRENT_TIMESTAMP WHERE ""IsActive"" = 0 AND ""ArchivedAt"" IS NULL;");
        }

        private static void FillBlankNames(DbConnection connection, DbTransaction? tx)
        {
            if (TableExists(connection, "Account", tx))
            {
                Execute(connection, tx, @"
UPDATE ""Bank""
SET ""Name"" = (
  SELECT a.""AccountName""
  FROM ""Account"" AS a
  WHERE a.""Id"" = ""Bank"".""AccountId""
    AND a.""CompanyId"" = ""Bank"".""CompanyId""
)
WHERE (""Name"" IS NULL OR TRIM(""Name"") = '')
  AND ""AccountId"" IS NOT NULL
  AND EXISTS (
    SELECT 1 FROM ""Account"" AS a
    WHERE a.""Id"" = ""Bank"".""AccountId""
      AND a.""CompanyId"" = ""Bank"".""CompanyId""
      AND a.""AccountName"" IS NOT NULL
      AND TRIM(a.""AccountName"") != ''
  );");
            }

            Execute(connection, tx, @"
UPDATE ""Bank""
SET ""Name"" = 'Bank ' || CAST(""Id"" AS TEXT)
WHERE ""Name"" IS NULL OR TRIM(""Name"") = '';");
        }

        private static void DeduplicateNames(DbConnection connection, DbTransaction? tx)
        {
            // Append " ({Id})" so renames never collide with an existing "X (2)" or each other.
            Execute(connection, tx, @"
UPDATE ""Bank""
SET ""Name"" = ""Name"" || ' (' || CAST(""Id"" AS TEXT) || ')'
WHERE EXISTS (
  SELECT 1 FROM ""Bank"" AS other
  WHERE other.""CompanyId"" = ""Bank"".""CompanyId""
    AND lower(other.""Name"") = lower(""Bank"".""Name"")
    AND other.""Id"" < ""Bank"".""Id""
);");
        }

        private static void CollapseMultipleDefaults(DbConnection connection, DbTransaction? tx)
        {
            Execute(connection, tx, @"
UPDATE ""Bank"" SET ""IsDefaultForReceipts"" = 0
WHERE ""IsDefaultForReceipts"" = 1
  AND ""Id"" NOT IN (
    SELECT ""Id"" FROM (
      SELECT MIN(""Id"") AS ""Id"" FROM ""Bank"" WHERE ""IsDefaultForReceipts"" = 1 GROUP BY ""CompanyId""
    )
  );");
            Execute(connection, tx, @"
UPDATE ""Bank"" SET ""IsDefaultForPayments"" = 0
WHERE ""IsDefaultForPayments"" = 1
  AND ""Id"" NOT IN (
    SELECT ""Id"" FROM (
      SELECT MIN(""Id"") AS ""Id"" FROM ""Bank"" WHERE ""IsDefaultForPayments"" = 1 GROUP BY ""CompanyId""
    )
  );");
        }

        private static void RepairInvalidAndSharedAccounts(DbConnection connection, DbTransaction? tx, ILogger? logger)
        {
            if (!TableExists(connection, "Account", tx))
                return;

            var banks = LoadBanks(connection, tx);
            var usedCodesByCompany = LoadUsedAccountCodes(connection, tx);
            var templateByCompany = LoadCoaTemplateIds(connection, tx);
            var banksByAccount = new Dictionary<(int CompanyId, int AccountId), List<BankRow>>();

            foreach (var bank in banks)
            {
                if (!bank.AccountId.HasValue)
                {
                    TryAssignDedicatedAccount(connection, tx, bank, usedCodesByCompany, templateByCompany, logger,
                        reason: "null AccountId");
                    continue;
                }

                var account = LoadAccount(connection, tx, bank.CompanyId, bank.AccountId.Value);
                if (account == null)
                {
                    TryAssignDedicatedAccount(connection, tx, bank, usedCodesByCompany, templateByCompany, logger,
                        reason: "missing Account row");
                    continue;
                }

                var isSummary = HasChildren(connection, tx, account.Id, account.CompanyId);
                var isCash = account.IsCash || ParentIsCash(connection, tx, account);
                var hasHistory = HasGlHistory(connection, tx, account.Id);

                if (isSummary || !isCash)
                {
                    if (hasHistory)
                    {
                        logger?.LogWarning(
                            "Bank {BankId} (company {CompanyId}) points at non-eligible account {AccountId} with GL history; left unchanged for preflight.",
                            bank.Id, bank.CompanyId, account.Id);
                        continue;
                    }

                    TryAssignDedicatedAccount(connection, tx, bank, usedCodesByCompany, templateByCompany, logger,
                        reason: isSummary ? "summary account" : "non-cash account");
                    continue;
                }

                var key = (bank.CompanyId, bank.AccountId.Value);
                if (!banksByAccount.TryGetValue(key, out var list))
                {
                    list = new List<BankRow>();
                    banksByAccount[key] = list;
                }
                list.Add(bank);
            }

            foreach (var pair in banksByAccount)
            {
                if (pair.Value.Count <= 1)
                    continue;

                pair.Value.Sort((a, b) => a.Id.CompareTo(b.Id));
                // Lowest id keeps the shared account; others get a dedicated empty account.
                for (var i = 1; i < pair.Value.Count; i++)
                {
                    TryAssignDedicatedAccount(connection, tx, pair.Value[i], usedCodesByCompany, templateByCompany, logger,
                        reason: "shared account");
                }
            }
        }

        private static void TryAssignDedicatedAccount(
            DbConnection connection,
            DbTransaction? tx,
            BankRow bank,
            Dictionary<int, HashSet<string>> usedCodesByCompany,
            Dictionary<int, string> templateByCompany,
            ILogger? logger,
            string reason)
        {
            var cashSummaryId = FindCashSummaryAccountId(connection, tx, bank.CompanyId);
            if (!cashSummaryId.HasValue)
            {
                logger?.LogWarning(
                    "Bank {BankId} (company {CompanyId}) needs a dedicated account ({Reason}) but no cash summary account exists; left unchanged.",
                    bank.Id, bank.CompanyId, reason);
                return;
            }

            if (!usedCodesByCompany.TryGetValue(bank.CompanyId, out var used))
            {
                used = new HashSet<string>(StringComparer.Ordinal);
                usedCodesByCompany[bank.CompanyId] = used;
            }

            templateByCompany.TryGetValue(bank.CompanyId, out var templateId);
            var code = BankCashAccountCodeAllocator.Allocate(used, templateId);
            used.Add(code);

            var displayName = string.IsNullOrWhiteSpace(bank.Name)
                ? "Bank " + bank.Id.ToString(CultureInfo.InvariantCulture)
                : bank.Name;

            var newAccountId = InsertAccount(connection, tx, bank.CompanyId, cashSummaryId.Value, code, displayName);
            Execute(connection, tx,
                @"UPDATE ""Bank"" SET ""AccountId"" = $accountId WHERE ""Id"" = $bankId;",
                ("$accountId", newAccountId),
                ("$bankId", bank.Id));

            logger?.LogInformation(
                "Bank {BankId} (company {CompanyId}) repointed to dedicated account {AccountId} ({Code}) because {Reason}.",
                bank.Id, bank.CompanyId, newAccountId, code, reason);
        }

        private static void EnsureNoNullAccountIds(DbConnection connection, DbTransaction? tx)
        {
            using var cmd = connection.CreateCommand();
            if (tx != null) cmd.Transaction = tx;
            cmd.CommandText = @"SELECT COUNT(*) FROM ""Bank"" WHERE ""AccountId"" IS NULL;";
            var remaining = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (remaining > 0)
            {
                throw new InvalidOperationException(
                    "Bank.AccountId still NULL after cleanup; run scripts/d1/bank-preflight.sql and fix before deploying 0032.");
            }
        }

        private static int InsertAccount(
            DbConnection connection,
            DbTransaction? tx,
            int companyId,
            int parentAccountId,
            string accountCode,
            string accountName)
        {
            using (var cmd = connection.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO ""Account"" (
  ""AccountClassId"", ""ParentAccountId"", ""DrOrCrSide"", ""CompanyId"",
  ""AccountCode"", ""AccountName"", ""Description"", ""IsCash"", ""IsContraAccount""
) VALUES (
  1, $parentId, 1, $companyId, $code, $name, NULL, 0, 0
);";
                AddParam(cmd, "$parentId", parentAccountId);
                AddParam(cmd, "$companyId", companyId);
                AddParam(cmd, "$code", accountCode);
                AddParam(cmd, "$name", accountName);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandText = "SELECT last_insert_rowid();";
                return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        private static int? FindCashSummaryAccountId(DbConnection connection, DbTransaction? tx, int companyId)
        {
            using var cmd = connection.CreateCommand();
            if (tx != null) cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT a.""Id""
FROM ""Account"" a
WHERE a.""CompanyId"" = $companyId
  AND a.""IsCash"" = 1
  AND EXISTS (
    SELECT 1 FROM ""Account"" child
    WHERE child.""ParentAccountId"" = a.""Id"" AND child.""CompanyId"" = a.""CompanyId""
  )
ORDER BY CASE WHEN a.""AccountCode"" = '10110' THEN 0 ELSE 1 END, a.""AccountCode""
LIMIT 1;";
            AddParam(cmd, "$companyId", companyId);
            var value = cmd.ExecuteScalar();
            return value == null || value == DBNull.Value
                ? null
                : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private static List<BankRow> LoadBanks(DbConnection connection, DbTransaction? tx)
        {
            var rows = new List<BankRow>();
            using var cmd = connection.CreateCommand();
            if (tx != null) cmd.Transaction = tx;
            cmd.CommandText = @"SELECT ""Id"", ""CompanyId"", ""AccountId"", ""Name"" FROM ""Bank"" ORDER BY ""CompanyId"", ""Id"";";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new BankRow(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
            return rows;
        }

        private static Dictionary<int, HashSet<string>> LoadUsedAccountCodes(DbConnection connection, DbTransaction? tx)
        {
            var map = new Dictionary<int, HashSet<string>>();
            using var cmd = connection.CreateCommand();
            if (tx != null) cmd.Transaction = tx;
            cmd.CommandText = @"SELECT ""CompanyId"", ""AccountCode"" FROM ""Account"";";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var companyId = reader.GetInt32(0);
                var code = reader.GetString(1);
                if (!map.TryGetValue(companyId, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    map[companyId] = set;
                }
                set.Add(code);
            }
            return map;
        }

        private static Dictionary<int, string> LoadCoaTemplateIds(DbConnection connection, DbTransaction? tx)
        {
            var map = new Dictionary<int, string>();
            if (!TableExists(connection, "CompanySetting", tx)
                || !ColumnExists(connection, "CompanySetting", "CoaTemplateId", tx))
            {
                return map;
            }

            using var cmd = connection.CreateCommand();
            if (tx != null) cmd.Transaction = tx;
            cmd.CommandText = @"SELECT ""CompanyId"", ""CoaTemplateId"" FROM ""CompanySetting"";";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(1))
                    continue;
                map[reader.GetInt32(0)] = reader.GetString(1);
            }
            return map;
        }

        private static AccountRow? LoadAccount(DbConnection connection, DbTransaction? tx, int companyId, int accountId)
        {
            using var cmd = connection.CreateCommand();
            if (tx != null) cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT ""Id"", ""CompanyId"", ""ParentAccountId"", ""IsCash"", ""AccountCode""
FROM ""Account""
WHERE ""Id"" = $id AND ""CompanyId"" = $companyId;";
            AddParam(cmd, "$id", accountId);
            AddParam(cmd, "$companyId", companyId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return null;
            return new AccountRow(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.GetInt32(3) != 0,
                reader.GetString(4));
        }

        private static bool ParentIsCash(DbConnection connection, DbTransaction? tx, AccountRow account)
        {
            if (!account.ParentAccountId.HasValue)
                return false;
            using var cmd = connection.CreateCommand();
            if (tx != null) cmd.Transaction = tx;
            cmd.CommandText = @"SELECT ""IsCash"" FROM ""Account"" WHERE ""Id"" = $id AND ""CompanyId"" = $companyId;";
            AddParam(cmd, "$id", account.ParentAccountId.Value);
            AddParam(cmd, "$companyId", account.CompanyId);
            var value = cmd.ExecuteScalar();
            return value != null && value != DBNull.Value && Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0;
        }

        private static bool HasChildren(DbConnection connection, DbTransaction? tx, int accountId, int companyId)
        {
            using var cmd = connection.CreateCommand();
            if (tx != null) cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT 1 FROM ""Account""
WHERE ""ParentAccountId"" = $id AND ""CompanyId"" = $companyId
LIMIT 1;";
            AddParam(cmd, "$id", accountId);
            AddParam(cmd, "$companyId", companyId);
            return cmd.ExecuteScalar() != null;
        }

        private static bool HasGlHistory(DbConnection connection, DbTransaction? tx, int accountId)
        {
            if (!TableExists(connection, "GeneralLedgerLine", tx))
                return false;
            using var cmd = connection.CreateCommand();
            if (tx != null) cmd.Transaction = tx;
            cmd.CommandText = @"SELECT 1 FROM ""GeneralLedgerLine"" WHERE ""AccountId"" = $id LIMIT 1;";
            AddParam(cmd, "$id", accountId);
            return cmd.ExecuteScalar() != null;
        }

        private static void Execute(DbConnection connection, DbTransaction? tx, string sql, params (string Name, object Value)[] parameters)
        {
            using var cmd = connection.CreateCommand();
            if (tx != null) cmd.Transaction = tx;
            cmd.CommandText = sql;
            foreach (var (name, value) in parameters)
                AddParam(cmd, name, value);
            cmd.ExecuteNonQuery();
        }

        private static void AddParam(DbCommand cmd, string name, object value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            cmd.Parameters.Add(p);
        }

        private static bool TableExists(DbConnection connection, string tableName, DbTransaction? tx)
        {
            using var cmd = connection.CreateCommand();
            if (tx != null) cmd.Transaction = tx;
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$tableName;";
            AddParam(cmd, "$tableName", tableName);
            return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
        }

        private static bool ColumnExists(DbConnection connection, string tableName, string columnName, DbTransaction? tx)
        {
            using var cmd = connection.CreateCommand();
            if (tx != null) cmd.Transaction = tx;
            cmd.CommandText = $"PRAGMA table_info(\"{tableName}\");";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private sealed record BankRow(int Id, int CompanyId, int? AccountId, string? Name);
        private sealed record AccountRow(int Id, int CompanyId, int? ParentAccountId, bool IsCash, string AccountCode);
    }
}
