// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;

namespace Services.Recurring
{
    /// <summary>
    /// #482: turns a claimed sales-invoice <see cref="Core.Domain.Recurring.RecurringOccurrence"/> into a
    /// draft invoice and, when the schedule asks for auto-issue and every guard passes, issues it.
    ///
    /// The service processes an occurrence that has already been created and claimed (lease held) — it never
    /// generates directly from a schedule. Idempotency rests on the unique
    /// (CompanyId, RecurringScheduleId, ScheduledRunDate) occurrence key; a replay that finds the occurrence
    /// already carries a document is a no-op. The worker and lease flow live in #480.
    /// </summary>
    public interface IRecurringInvoiceGenerationService
    {
        /// <summary>
        /// Ensures the single idempotent occurrence for (schedule, scheduledRunDate) exists and returns its id,
        /// claiming it for immediate processing by <paramref name="leaseOwner"/>. Used by a manual "run now" so it
        /// goes through the same occurrence path as the worker rather than bypassing it. If the occurrence already
        /// exists it is returned as-is (not re-claimed when another owner holds a live lease).
        /// </summary>
        int EnsureOccurrence(int scheduleId, DateTime scheduledRunDate, string leaseOwner, DateTime leaseUntil);

        /// <summary>
        /// Processes a claimed occurrence: builds the draft invoice, links it, and either leaves it as a draft or
        /// issues it per the schedule's action and the #482 guard list. The caller must hold the occurrence lease.
        /// </summary>
        RecurringGenerationResult ProcessOccurrence(int occurrenceId, string leaseOwner);

        /// <summary>
        /// #480: runs the schedule's next due occurrence manually and advances the series by one.
        ///
        /// A manual run always generates the schedule's current
        /// <see cref="Core.Domain.Recurring.RecurringSchedule.NextRunDate"/>; it is rejected when there is no next
        /// run date (an exhausted or never-scheduled schedule has nothing to run — recover a failed past occurrence
        /// through <see cref="RetryOccurrence"/> instead). When <paramref name="runDate"/> is supplied it must equal
        /// that next date (a guard against silently skipping intervening periods) or
        /// <see cref="InvalidRunDateException"/> is thrown. The single idempotent occurrence for that date is
        /// ensured and processed through <see cref="EnsureOccurrence"/> + <see cref="ProcessOccurrence"/>, then
        /// <c>NextRunDate</c> advances by exactly one calendar step, respecting the end date and occurrence limit.
        /// A failed attempt still advances the next date so the series does not stall, but it does not end the
        /// schedule: the failed occurrence stays retryable via <see cref="RetryOccurrence"/>.
        ///
        /// Before any occurrence is created, if the schedule's customer has been archived the schedule is paused and
        /// flagged for attention and no document is produced (an <see cref="RecurringGenerationOutcome.Aborted"/>
        /// result carrying the reason). Throws <see cref="InvalidRunDateException"/> for a paused/ended schedule, a
        /// mismatched run date, or when there is no next run date set.
        /// </summary>
        RecurringGenerationResult RunSchedule(int scheduleId, DateTime? runDate, string leaseOwner, DateTime leaseUntil);

        /// <summary>
        /// #480: retries one specific occurrence by id. Re-claims the occurrence (a failed or crashed-mid-run row)
        /// under <paramref name="leaseOwner"/> and processes it again against its own idempotency key. This never
        /// advances the schedule's <see cref="Core.Domain.Recurring.RecurringSchedule.NextRunDate"/>: a retry of an
        /// earlier run must not move the series. If the customer has since been archived the schedule is paused
        /// instead of producing a document. When the retried occurrence was the schedule's last allowed run — the
        /// series is exhausted but the schedule was left Active by the failed final run — a now-successful retry
        /// finalizes the schedule as <see cref="Core.Domain.Recurring.RecurringScheduleStatus.Ended"/>. Returns
        /// <see cref="RecurringGenerationOutcome.Aborted"/> when the occurrence cannot be retried (missing, already
        /// completed, or held by a live lease).
        /// </summary>
        RecurringGenerationResult RetryOccurrence(int occurrenceId, string leaseOwner, DateTime leaseUntil);

        /// <summary>
        /// #484: skips the schedule's current next due occurrence. Records the <paramref name="reason"/> and the
        /// acting user on the occurrence, marks it <see cref="Core.Domain.Recurring.RecurringOccurrenceStatus.Skipped"/>,
        /// generates no document, and advances the series by exactly one calendar step — without changing the
        /// calendar anchor (later dates do not shift). Exactly-once: like a run, the advance is keyed on the
        /// occurrence so a replay cannot skip-and-advance twice. Rejected with
        /// <see cref="InvalidRunDateException"/> for a paused/ended schedule, when there is no next run date, or when
        /// the named <paramref name="runDate"/> does not equal the current next run date. If the customer has been
        /// archived the schedule is paused instead (an <see cref="RecurringGenerationOutcome.Aborted"/> result).
        /// </summary>
        RecurringGenerationResult SkipNextOccurrence(int scheduleId, DateTime? runDate, string actingUser, string reason, string leaseOwner, DateTime leaseUntil);
    }
}
