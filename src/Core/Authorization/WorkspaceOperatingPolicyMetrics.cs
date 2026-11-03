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

        /// <summary>
        /// Registers the durable "active invariant incidents" gauge, whose value is produced by
        /// <paramref name="observer"/> each time the meter is observed. Wired from the Api layer where a
        /// database query can count workspaces currently in a violating state. Idempotent: the first
        /// registration creates the gauge; later calls just replace the observed value source. Exposing
        /// current state separately from the new-incident counters lets monitoring see a violation that
        /// persists across a restart even when no new incident is being recorded.
        /// </summary>
        public static void RegisterActiveIncidentObserver(System.Func<int> observer)
        {
            var isFirst = s_activeIncidentObserver == null;
            s_activeIncidentObserver = observer;
            if (isFirst)
            {
                s_meter.CreateObservableGauge(
                    "workspace.active_invariant_incidents",
                    () => s_activeIncidentObserver?.Invoke() ?? 0,
                    description: "Workspaces currently in a committed invariant-violating state (durable current state).");
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
