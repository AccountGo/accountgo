// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Api.Data;
using Core.Authorization;
using Core.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Api.Service
{
    /// <summary>
    /// Durable, idempotent markers for workspace invariant detection, persisted as audit rows so they
    /// commit atomically with the surrounding policy transaction and survive process restarts. Audit rows
    /// keep these additive and provider-neutral (no schema migration across SQLite/Postgres/SQL Server).
    ///
    /// Three concepts are kept deliberately distinct:
    ///  - the first qualifying committed action per workspace (measurement — see
    ///    <see cref="Core.Authorization.WorkspaceFirstActionEvidence"/>),
    ///  - the current committed invariant violation (state — see
    ///    <see cref="WorkspacePolicyTransaction.ComputeViolationSignature"/>), and
    ///  - whether a given violation signature has already been reported (so re-scans do not re-count the
    ///    same persisted violation as a newly committed one).
    /// </summary>
    public static class WorkspacePolicyDurableEvidence
    {
        public const string ViolationReportedTable = "__WorkspaceInvariantViolationReported";

        /// <summary>True when a first-useful-action marker already exists for the workspace.</summary>
        public static bool HasFirstUsefulAction(ApiDbContext db, int companyId) =>
            db.AuditLogs.IgnoreQueryFilters()
                .Any(a => a.CompanyId == companyId
                          && a.TableName == WorkspaceFirstActionEvidence.FirstUsefulActionTable);

        /// <summary>
        /// Returns true when the given violation signature has NOT yet been reported for the workspace,
        /// and records it so subsequent scans of the same persisted violation do not re-count it. Returns
        /// false when the signature was already reported (idempotent). A null/empty signature means "no
        /// current violation" and is never recorded.
        /// </summary>
        public static bool TryMarkViolationNewlyReported(
            ApiDbContext db,
            int companyId,
            string? violationSignature,
            DateTime observedAtUtc)
        {
            if (companyId <= 0 || string.IsNullOrWhiteSpace(violationSignature))
                return false;

            var alreadyReported = db.AuditLogs
                .IgnoreQueryFilters()
                .Any(a => a.CompanyId == companyId
                          && a.TableName == ViolationReportedTable
                          && a.FieldName == violationSignature);
            if (alreadyReported)
                return false;

            db.AuditLogs.Add(new AuditLog
            {
                CompanyId = companyId,
                UserName = "system",
                AuditEventDateUTC = observedAtUtc,
                AuditEventType = (int)AuditEventTypes.Added,
                TableName = ViolationReportedTable,
                RecordId = companyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FieldName = violationSignature,
                OriginalValue = string.Empty,
                NewValue = System.Text.Json.JsonSerializer.Serialize(new
                {
                    companyId,
                    violationSignature,
                    observedAtUtc
                })
            });

            return true;
        }
    }
}
