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
        /// #480: runs one scheduled occurrence manually and advances the series.
        ///
        /// Resolves the run date (the schedule's <see cref="Core.Domain.Recurring.RecurringSchedule.NextRunDate"/>
        /// when none is supplied), ensures and processes the single idempotent occurrence for it through
        /// <see cref="EnsureOccurrence"/> + <see cref="ProcessOccurrence"/>, and then — only when the run moved the
        /// series forward (the run date is on or after the current <c>NextRunDate</c>) — advances
        /// <c>NextRunDate</c> to the next calendar date, respecting the end date and occurrence limit. A failed
        /// attempt still advances the series: the failed occurrence is retried against its own key via
        /// <see cref="RetryOccurrence"/>, never by rewinding <c>NextRunDate</c>.
        ///
        /// Before any occurrence is created, if the schedule's customer has been archived the schedule is paused and
        /// flagged for attention and no document is produced (a <see cref="RecurringGenerationOutcome.Aborted"/>
        /// result carrying the reason). Throws <see cref="InvalidRunDateException"/> for a paused/ended schedule, an
        /// off-calendar date, or one outside the schedule's window.
        /// </summary>
        RecurringGenerationResult RunSchedule(int scheduleId, DateTime? runDate, string leaseOwner, DateTime leaseUntil);

        /// <summary>
        /// #480: retries one specific occurrence by id. Re-claims the occurrence (a failed or crashed-mid-run row)
        /// under <paramref name="leaseOwner"/> and processes it again against its own idempotency key. This never
        /// advances the schedule's <see cref="Core.Domain.Recurring.RecurringSchedule.NextRunDate"/>: a retry of an
        /// earlier run must not move the series. Returns <see cref="RecurringGenerationOutcome.Aborted"/> when the
        /// occurrence cannot be retried (missing, already completed, or held by a live lease).
        /// </summary>
        RecurringGenerationResult RetryOccurrence(int occurrenceId, string leaseOwner, DateTime leaseUntil);
    }
}
