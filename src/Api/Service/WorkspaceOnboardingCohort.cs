// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Api.Data;
using Core.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Api.Service
{
    /// <summary>
    /// Durable, deduplicated onboarding cohort evidence for the new business/freelance solo flow, used for
    /// true cohort-conversion analysis (as distinct from the throughput-ratio counters).
    ///
    /// A START is recorded at a clear journey boundary — the moment the owner enters the guided setup — and
    /// is INDEPENDENT of whether initialization ever succeeds. A COMPLETION is recorded only after a
    /// successful initialization. Because the two are written separately, an abandoned or never-finished
    /// start leaves a start row with no completion, so cohort conversion correctly counts dropouts (it is
    /// NOT structurally pinned to 100%).
    ///
    /// Both events are deduplicated per workspace: the start is recorded at most once (it is idempotent
    /// across the signup boundary and a later initialize attempt, and across retries), and the completion
    /// at most once (a repeat initialization cannot inflate completions). Rows live in the immutable
    /// <c>__WorkspaceOnboardingCohort</c> audit table.
    /// </summary>
    public static class WorkspaceOnboardingCohort
    {
        public const string AuditTableName = "__WorkspaceOnboardingCohort";
        public const string EventStart = "OnboardingStart";
        public const string EventCompletion = "OnboardingCompletion";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private static bool Exists(ApiDbContext db, int companyId, string eventKind) =>
            db.AuditLogs
                .IgnoreQueryFilters()
                .Any(a => a.CompanyId == companyId
                          && a.TableName == AuditTableName
                          && a.RecordId == companyId.ToString(CultureInfo.InvariantCulture)
                          && a.FieldName == eventKind);

        /// <summary>
        /// Records the durable cohort START once per workspace. Safe to call at the signup boundary and
        /// again at a later initialize attempt — the second call is a no-op. The row persists even if the
        /// workspace is never initialized, so dropouts remain in the denominator.
        /// </summary>
        public static bool RecordStart(ApiDbContext db, int companyId, string ownerUserId, string flow, string boundary)
        {
            if (Exists(db, companyId, EventStart))
                return false;

            db.AuditLogs.Add(new AuditLog
            {
                CompanyId = companyId,
                UserName = ownerUserId,
                AuditEventDateUTC = DateTime.UtcNow,
                AuditEventType = (int)AuditEventTypes.Added,
                TableName = AuditTableName,
                RecordId = companyId.ToString(CultureInfo.InvariantCulture),
                FieldName = EventStart,
                OriginalValue = EventStart,
                NewValue = JsonSerializer.Serialize(new
                {
                    eventKind = EventStart,
                    flow,
                    boundary,
                    companyId,
                    startedAtUtc = DateTime.UtcNow
                }, JsonOptions)
            });
            return true;
        }

        /// <summary>
        /// Records the durable cohort COMPLETION once per workspace, after a successful initialization.
        /// A repeat/duplicate initialization cannot write a second completion.
        /// </summary>
        public static bool RecordCompletion(ApiDbContext db, int companyId, string ownerUserId, string flow, string collaborationState, long policyRevision)
        {
            if (Exists(db, companyId, EventCompletion))
                return false;

            db.AuditLogs.Add(new AuditLog
            {
                CompanyId = companyId,
                UserName = ownerUserId,
                AuditEventDateUTC = DateTime.UtcNow,
                AuditEventType = (int)AuditEventTypes.Added,
                TableName = AuditTableName,
                RecordId = companyId.ToString(CultureInfo.InvariantCulture),
                FieldName = EventCompletion,
                OriginalValue = EventCompletion,
                NewValue = JsonSerializer.Serialize(new
                {
                    eventKind = EventCompletion,
                    flow,
                    status = "success",
                    companyId,
                    collaborationState,
                    policyRevision,
                    completedAtUtc = DateTime.UtcNow
                }, JsonOptions)
            });
            return true;
        }
    }
}
