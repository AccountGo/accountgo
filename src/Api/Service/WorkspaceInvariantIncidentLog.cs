// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Api.Data;
using Core.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Api.Service
{
    /// <summary>
    /// Durable, append-only incident log for committed invariant violations. It keeps three concerns
    /// distinct, as required for honest measurement:
    ///  - persistent current-violation STATE (reconstructed from the latest event per workspace),
    ///  - a detected INCIDENT (a new append-only "incident" row → one counter increment), and
    ///  - RESOLUTION (an append-only "resolved" row), so a violation that is fixed and later reappears is
    ///    a new incident rather than being suppressed forever.
    ///
    /// All reconciliation runs under the per-company coordination lock held by the caller, so the
    /// read-latest-then-append is serialized against concurrent writers and detectors for that company —
    /// two contexts cannot both record the same incident. Rows are append-only AuditLogs (immutable),
    /// so the current state is derived, never mutated in place.
    /// </summary>
    public static class WorkspaceInvariantIncidentLog
    {
        public const string IncidentTable = "__WorkspaceInvariantIncident";

        public const string EventIncident = "incident";
        public const string EventResolved = "resolved";

        public enum ReconcileOutcome
        {
            /// <summary>No active violation and none newly observed, or the same active violation again.</summary>
            NoChange,
            /// <summary>A newly observed violation that is not a continuation of an active one.</summary>
            NewIncident,
            /// <summary>A previously active violation that is no longer present.</summary>
            Resolved
        }

        /// <summary>The latest reconstructed state for a workspace.</summary>
        public readonly record struct CurrentState(bool HasActiveIncident, string? Signature, DateTime? SinceUtc);

        /// <summary>
        /// Returns the current violation state for a workspace by reading the latest incident-log event.
        /// </summary>
        public static CurrentState GetCurrentState(ApiDbContext db, int companyId)
        {
            var latest = db.AuditLogs.IgnoreQueryFilters()
                .Where(a => a.CompanyId == companyId && a.TableName == IncidentTable)
                .OrderByDescending(a => a.Id)
                .Select(a => new { a.FieldName, a.NewValue, a.AuditEventDateUTC })
                .FirstOrDefault();

            if (latest == null || latest.FieldName == EventResolved)
                return new CurrentState(false, null, null);

            return new CurrentState(true, latest.FieldName, latest.AuditEventDateUTC);
        }

        /// <summary>
        /// Reconciles the currently-observed signature (null when consistent) against the latest state and
        /// appends an event when the state changes. Must be called under the company coordination lock and
        /// inside the caller's transaction; the caller commits, then emits counters for a NewIncident.
        /// </summary>
        public static ReconcileOutcome Reconcile(ApiDbContext db, int companyId, string? signature, DateTime observedAtUtc)
        {
            if (companyId <= 0)
                return ReconcileOutcome.NoChange;

            var state = GetCurrentState(db, companyId);

            if (string.IsNullOrWhiteSpace(signature))
            {
                // Consistent now. If a violation was active, record its resolution so a future recurrence
                // counts again; otherwise nothing to do.
                if (!state.HasActiveIncident)
                    return ReconcileOutcome.NoChange;

                Append(db, companyId, EventResolved, state.Signature, observedAtUtc);
                return ReconcileOutcome.Resolved;
            }

            // A violation is present. If the same signature is already the active incident, this is a
            // continuation — do not re-count. A different signature, or no active incident (first time or
            // after a resolution), is a new incident.
            if (state.HasActiveIncident && string.Equals(state.Signature, signature, StringComparison.Ordinal))
                return ReconcileOutcome.NoChange;

            Append(db, companyId, signature, signature, observedAtUtc);
            return ReconcileOutcome.NewIncident;
        }

        /// <summary>
        /// Counts workspaces whose latest incident-log event is an unresolved incident, i.e. that are
        /// currently in a violating state. This is durable current-state, independent of the process-local
        /// new-incident counters (which reset on restart), so monitoring can see a violation that persists
        /// across restarts even when no new incident is being recorded.
        /// </summary>
        public static int CountWorkspacesWithActiveIncident(ApiDbContext db)
        {
            // Latest event per company; active when that event is not a "resolved" marker.
            var latestPerCompany =
                from a in db.AuditLogs.IgnoreQueryFilters()
                where a.TableName == IncidentTable
                group a by a.CompanyId into g
                select g.OrderByDescending(x => x.Id).Select(x => x.FieldName).FirstOrDefault();

            return latestPerCompany.Count(fieldName => fieldName != null && fieldName != EventResolved);
        }

        private static void Append(ApiDbContext db, int companyId, string fieldName, string? signature, DateTime whenUtc)
        {
            db.AuditLogs.Add(new AuditLog
            {
                CompanyId = companyId,
                UserName = "system",
                AuditEventDateUTC = whenUtc,
                AuditEventType = (int)AuditEventTypes.Added,
                TableName = IncidentTable,
                // FieldName carries the event discriminator: a violation signature for an incident, or
                // the literal "resolved" marker. Current state is the latest row's FieldName.
                RecordId = companyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FieldName = fieldName,
                OriginalValue = string.Empty,
                NewValue = System.Text.Json.JsonSerializer.Serialize(new { companyId, signature, whenUtc })
            });
            db.SaveChanges();
        }
    }
}
