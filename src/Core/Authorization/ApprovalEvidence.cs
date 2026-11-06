// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Core.Domain.Auditing;

namespace Core.Authorization
{
    public static class ApprovalEvidence
    {
        public const string ApprovalTable = "__AccessDelegationApproval";
        public const string StateTable = "__AccessDelegationApprovalState";
        public const string ApprovalStateUniqueIndex = "IX_AuditLog_ApprovalState";

        public static IQueryable<AuditLog> Available(IQueryable<AuditLog> rows, int companyId) =>
            rows.Where(a => a.CompanyId == companyId && a.TableName == ApprovalTable && a.OriginalValue == "Approved" &&
                !rows.Any(s => s.CompanyId == a.CompanyId && s.TableName == StateTable && s.RecordId == a.RecordId));

        public static bool IsValidApproval(IQueryable<AuditLog> rows, AuditLog grant, PersistedAccessApprovalPayload payload,
            int companyId, DateTimeOffset now, string operationKey = null, string resourceId = null, string fingerprint = null) =>
            grant != null && payload != null && grant.CompanyId == companyId && grant.TableName == ApprovalTable &&
            grant.OriginalValue == "Approved" && payload.CompanyId == companyId &&
            payload.Status == "Approved" && payload.ExpiresAtUtc > now &&
            (string.IsNullOrWhiteSpace(operationKey) || string.Equals(payload.OperationKey, operationKey, StringComparison.Ordinal)) &&
            (string.IsNullOrWhiteSpace(resourceId) || string.Equals(payload.ResourceId, resourceId, StringComparison.Ordinal)) &&
            (string.IsNullOrWhiteSpace(fingerprint) || string.Equals(payload.ResourceFingerprint, fingerprint, StringComparison.Ordinal)) &&
            !rows.Any(state => state.CompanyId == companyId && state.TableName == StateTable && state.RecordId == grant.RecordId);

        public static bool WasConsumedForJournal(IQueryable<AuditLog> rows, int companyId, string requestId, int journalId)
        {
            if (string.IsNullOrWhiteSpace(requestId) || journalId <= 0) return false;
            var grant = rows.FirstOrDefault(a => a.CompanyId == companyId && a.TableName == ApprovalTable && a.RecordId == requestId &&
                rows.Any(s => s.CompanyId == a.CompanyId && s.TableName == StateTable && s.RecordId == a.RecordId && s.OriginalValue == "Consumed"));
            if (grant == null || string.IsNullOrWhiteSpace(grant.NewValue)) return false;
            try
            {
                var payload = JsonSerializer.Deserialize<PersistedAccessApprovalPayload>(grant.NewValue);
                return payload?.OperationKey == AccessOperationCatalog.FinancialsJournalPost &&
                    payload.ResourceId == journalId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (JsonException) { return false; }
        }

        /// <summary>
        /// True when the exception chain is a unique violation on the approval-state index
        /// (Postgres <c>IX_AuditLog_ApprovalState</c> or the SQLite equivalent).
        /// </summary>
        public static bool IsApprovalStateUniqueViolation(Exception error)
        {
            for (Exception current = error; current != null; current = current.InnerException)
            {
                var type = current.GetType();
                if (string.Equals(type.FullName, "Npgsql.PostgresException", StringComparison.Ordinal))
                {
                    var sqlState = type.GetProperty("SqlState", BindingFlags.Public | BindingFlags.Instance)?.GetValue(current) as string;
                    var constraint = type.GetProperty("ConstraintName", BindingFlags.Public | BindingFlags.Instance)?.GetValue(current) as string;
                    if (sqlState == "23505" && constraint == ApprovalStateUniqueIndex)
                        return true;
                }

                if (string.Equals(type.FullName, "Microsoft.Data.Sqlite.SqliteException", StringComparison.Ordinal))
                {
                    var extended = type.GetProperty("SqliteExtendedErrorCode", BindingFlags.Public | BindingFlags.Instance)?.GetValue(current);
                    if (extended is int code && code == 2067 &&
                        current.Message.Contains("AuditLog.CompanyId, AuditLog.TableName, AuditLog.RecordId", StringComparison.Ordinal))
                        return true;
                }
            }

            return false;
        }

        public static AuditLog State(AuditLog grant, string status, string actorUserId)
        {
            PersistedAccessApprovalPayload payload = null;
            try { if (!string.IsNullOrWhiteSpace(grant.NewValue)) payload = JsonSerializer.Deserialize<PersistedAccessApprovalPayload>(grant.NewValue); }
            catch (JsonException) { }
            return new AuditLog
            {
                CompanyId = grant.CompanyId,
                UserName = actorUserId ?? "system",
                AuditEventDateUTC = DateTime.UtcNow,
                AuditEventType = (int)AuditEventTypes.Added,
                TableName = StateTable,
                RecordId = grant.RecordId,
                FieldName = "Status",
                OriginalValue = status,
                NewValue = JsonSerializer.Serialize(new { payload?.OperationKey, payload?.ResourceId })
            };
        }
    }
}
