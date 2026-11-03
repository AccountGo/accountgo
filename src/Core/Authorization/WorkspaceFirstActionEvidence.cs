// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Core.Data;
using Core.Domain.Auditing;

namespace Core.Authorization
{
    /// <summary>
    /// Durable, idempotent "first useful action" marker for a workspace, persisted as an audit row so it
    /// commits atomically with the financial transaction that produced the action and survives restarts.
    ///
    /// "First" is established by the marker's absence, uniquely per workspace, across journal, invoice and
    /// bill paths — not by counting posted documents. This makes the measurement reliable under retries
    /// (the marker already exists), rollback (the marker insert rolls back with the action), concurrency
    /// (the per-company coordination lock held by the policy transaction serializes the check-and-insert)
    /// and restarts (the marker is durable).
    ///
    /// A qualifying committed action is the first successful post of an entitled accounting document:
    /// a posted journal entry, a posted sales invoice, or a posted bill.
    /// </summary>
    public static class WorkspaceFirstActionEvidence
    {
        public const string FirstUsefulActionTable = "__WorkspaceFirstUsefulAction";

        /// <summary>
        /// Records the first qualifying action for a workspace exactly once using the audit repository.
        /// Returns true when THIS call established the marker (genuinely first), false if one already
        /// existed. The repository is tenant-scoped to the acting company, so the existence check and
        /// insert are naturally company-scoped. Does not save; the caller's transaction owns the commit.
        /// </summary>
        public static bool TryRecordFirstUsefulAction(
            IRepository<AuditLog> auditLogRepo,
            int companyId,
            string actionKind,
            string? actorUserId,
            DateTime initializedAtUtc,
            DateTime committedAtUtc)
        {
            if (auditLogRepo == null || companyId <= 0)
                return false;

            var already = auditLogRepo.Table
                .Any(a => a.CompanyId == companyId && a.TableName == FirstUsefulActionTable);
            if (already)
                return false;

            var durationSeconds = (committedAtUtc - initializedAtUtc).TotalSeconds;
            if (durationSeconds < 0)
                durationSeconds = 0;

            auditLogRepo.Insert(new AuditLog
            {
                CompanyId = companyId,
                UserName = actorUserId ?? "system",
                AuditEventDateUTC = committedAtUtc,
                AuditEventType = (int)AuditEventTypes.Added,
                TableName = FirstUsefulActionTable,
                RecordId = companyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FieldName = "FirstUsefulAction",
                OriginalValue = string.Empty,
                NewValue = System.Text.Json.JsonSerializer.Serialize(new
                {
                    companyId,
                    actionKind,
                    actorUserId,
                    initializedAtUtc,
                    committedAtUtc,
                    durationSeconds
                })
            });

            return true;
        }
    }
}
