// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Diagnostics.Metrics;

namespace Core.Authorization
{
    /// <summary>
    /// Privacy-safe, low-cardinality telemetry for workspace operating policy, onboarding,
    /// authorizations, conflicts, and post-commit correctness invariant monitoring.
    /// Strictly excludes workspace IDs, user IDs, customer names, emails, financial amounts, and tokens.
    /// </summary>
    public static class WorkspaceOperatingPolicyMetrics
    {
        public const string MeterName = "AccountGo.WorkspacePolicy";
        public const string MeterVersion = "1.0.0";

        private static readonly Meter s_meter = new(MeterName, MeterVersion);

        private static System.Func<int>? s_activeIncidentObserver;
        private static DateTime? s_activeIncidentLastSuccessUtc;

        /// <summary>
        /// Registers the durable "active invariant incidents" gauges, whose values are produced by
        /// <paramref name="observer"/> each time the meter is observed. Wired from the Api layer where a
        /// database query can count workspaces currently in a violating state. Idempotent: the first
        /// registration creates the gauges; later calls just replace the observed value source.
        ///
        /// The observer MUST return a non-negative count on a successful query, or a negative value to
        /// signal that the measurement is unavailable (the query failed). Three series are exposed so
        /// monitoring can distinguish a genuine healthy zero from an unavailable/stale measurement:
        ///  - workspace.active_invariant_incidents: the current count, or -1 when unavailable. A healthy
        ///    zero is ONLY ever published by a successful query; a failure never masquerades as zero.
        ///  - workspace.active_invariant_incidents.query_failures: monotonically increasing count of failed
        ///    observations, to alert on.
        ///  - workspace.active_invariant_incidents.last_success_age_seconds: seconds since the last
        ///    successful observation (-1 until the first success), to alert on staleness.
        /// Exposing current state separately from the new-incident counters lets monitoring see a violation
        /// that persists across a restart even when no new incident is being recorded.
        /// </summary>
        public static void RegisterActiveIncidentObserver(System.Func<int> observer)
        {
            var isFirst = s_activeIncidentObserver == null;
            s_activeIncidentObserver = observer;
            if (isFirst)
            {
                s_meter.CreateObservableGauge(
                    "workspace.active_invariant_incidents",
                    ObserveActiveIncidentCount,
                    description: "Workspaces currently in a committed invariant-violating state (durable current state); -1 when the measurement is unavailable.");

                s_meter.CreateObservableGauge(
                    "workspace.active_invariant_incidents.last_success_age_seconds",
                    () =>
                    {
                        var last = s_activeIncidentLastSuccessUtc;
                        return last.HasValue ? Math.Max(0, (DateTime.UtcNow - last.Value).TotalSeconds) : -1d;
                    },
                    unit: "s",
                    description: "Seconds since the active-incident count was last measured successfully; -1 before the first success. Alert on staleness.");
            }
        }

        /// <summary>
        /// Monotonic count of failed active-incident observations. A non-zero/increasing value means the
        /// current-state measurement is unavailable — monitoring must NOT read a zero incident count as
        /// healthy while this is rising.
        /// </summary>
        public static readonly Counter<long> ActiveIncidentQueryFailures = s_meter.CreateCounter<long>(
            "workspace.active_invariant_incidents.query_failures",
            description: "Failed attempts to measure the active invariant-incident count. Nonzero means current-state monitoring is unavailable.");

        private static int ObserveActiveIncidentCount()
        {
            var observer = s_activeIncidentObserver;
            if (observer == null)
                return -1;
            try
            {
                var value = observer();
                if (value < 0)
                {
                    // The Api-side observer itself signalled unavailability.
                    ActiveIncidentQueryFailures.Add(1);
                    return -1;
                }
                s_activeIncidentLastSuccessUtc = DateTime.UtcNow;
                return value;
            }
            catch
            {
                // Never let a failed measurement publish a healthy-looking zero.
                ActiveIncidentQueryFailures.Add(1);
                return -1;
            }
        }

        public static readonly Counter<long> OnboardingStarts = s_meter.CreateCounter<long>(
            "workspace.onboarding.starts",
            description: "Number of workspace onboarding or initialization flows initiated.");

        public static readonly Counter<long> OnboardingCompletions = s_meter.CreateCounter<long>(
            "workspace.onboarding.completions",
            description: "Number of workspace onboarding or initialization flows completed and committed.");

        public static readonly Counter<long> Authorizations = s_meter.CreateCounter<long>(
            "workspace.policy.authorizations",
            description: "Number of policy-coordinated authorization decisions evaluated.");

        public static readonly Counter<long> Conflicts = s_meter.CreateCounter<long>(
            "workspace.policy.conflicts",
            description: "Number of expected policy concurrency conflicts (stale revision, competing invitation).");

        public static readonly Counter<long> UnauthorizedElevationsCommitted = s_meter.CreateCounter<long>(
            "workspace.unauthorized_elevations.committed",
            description: "CRITICAL: Correctness failure where unauthorized privilege elevation committed.");

        public static readonly Counter<long> InconsistentTransitionsCommitted = s_meter.CreateCounter<long>(
            "workspace.inconsistent_transitions.committed",
            description: "CRITICAL: Correctness failure where an inconsistent workspace state transition committed.");

        public static readonly Counter<long> PostCommitDetectorExceptions = s_meter.CreateCounter<long>(
            "workspace.policy.detector_exceptions",
            description: "Number of exceptions encountered during post-commit invariant evaluation.");

        public static readonly Histogram<double> FirstUsefulActionDuration = s_meter.CreateHistogram<double>(
            "workspace.first_useful_action.duration",
            unit: "s",
            description: "Duration in seconds from workspace initialization to first useful accounting action.");

        public static class FlowLabels
        {
            public const string LegacySolo = "legacy_solo";
            public const string NewWorkspace = "new_workspace";
            public const string ControlledRecovery = "controlled_recovery";
        }

        public static class OutcomeLabels
        {
            public const string Allow = "allow";
            public const string ExpectedDeny = "expected_deny";
            public const string UnexpectedFailure = "unexpected_failure";
        }

        public static class ConflictLabels
        {
            public const string StaleRevision = "stale_revision";
            public const string CompetingInvitation = "competing_invitation";
            public const string ConcurrentMutation = "concurrent_mutation";
        }

        public static class StatusLabels
        {
            public const string Success = "success";
            public const string Conflict = "conflict";
            public const string Ineligible = "ineligible";
        }

        public static class ActionLabels
        {
            public const string JournalPost = "journal_post";
            public const string SalesInvoice = "sales_invoice";
            public const string Bill = "bill";
        }
    }
}
