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
    }
}
