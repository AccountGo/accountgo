// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using Core.Domain.Recurring;

namespace Services.Recurring
{
    /// <summary>
    /// #484: management (CRUD + lifecycle) for sales-invoice recurring schedules, backing the UI. Generation and
    /// the series-advancement model live in <see cref="IRecurringInvoiceGenerationService"/>; this service never
    /// generates documents. Every method is tenant-scoped (the caller must have impersonated the company) and the
    /// lifecycle actions audit who did what. Pause/End leave existing invoices untouched; Resume refuses an archived
    /// customer and never triggers catch-up for missed periods.
    /// </summary>
    public interface IRecurringScheduleService
    {
        /// <summary>Lists schedules, optionally filtered by status, newest first. Includes customer name/archived.</summary>
        IReadOnlyList<RecurringScheduleSummary> ListSchedules(RecurringScheduleStatus? status, bool needsAttentionOnly);

        /// <summary>Full detail for one schedule, including its lines and the next few upcoming dates. Null when not found.</summary>
        RecurringScheduleDetail? GetSchedule(int scheduleId);

        /// <summary>Occurrence history for one schedule, newest first.</summary>
        IReadOnlyList<RecurringOccurrenceView> GetOccurrences(int scheduleId);

        /// <summary>
        /// The next <paramref name="count"/> run dates a schedule's calendar rules would produce, for the editor's
        /// preview. Pure calendar math anchored to the start date; honours end date and occurrence limit. Validates
        /// the cadence/day-of-month and throws <see cref="RecurringScheduleValidationException"/> on bad input.
        /// </summary>
        IReadOnlyList<DateTime> PreviewDates(
            RecurringCadence cadence, int interval, DateTime startDate, int? dayOfMonth,
            DateTime? endDate, int? maxOccurrences, int count);

        /// <summary>
        /// Creates a new Active sales-invoice schedule with its lines and initial NextRunDate (the first calendar
        /// date). Validates the customer belongs to the tenant and is not archived, the cadence/day-of-month, and at
        /// least one line. Audits the creation. Returns the new schedule id.
        /// </summary>
        int CreateSchedule(RecurringScheduleInput input, string actingUser);

        /// <summary>
        /// Updates an existing schedule's fields and lines. Recomputes NextRunDate from the new calendar rules when
        /// the schedule is Active and not yet started past its anchor. Audits the change. Throws
        /// <see cref="RecurringScheduleValidationException"/> for a missing/ended schedule or invalid input.
        /// </summary>
        void UpdateSchedule(RecurringScheduleInput input, string actingUser);

        /// <summary>
        /// Pauses an Active schedule: it stops generating. Existing invoices are untouched. Audits the pause and
        /// records the optional reason. No-op (idempotent) if already paused.
        /// </summary>
        void PauseSchedule(int scheduleId, string actingUser, string? reason);

        /// <summary>
        /// Resumes a Paused schedule back to Active WITHOUT catch-up: NextRunDate is recomputed to the next calendar
        /// date on or after today, so missed periods are not back-filled. Refuses when the customer is archived.
        /// Audits the resume. Throws <see cref="RecurringScheduleValidationException"/> when not resumable.
        /// </summary>
        void ResumeSchedule(int scheduleId, string actingUser);

        /// <summary>
        /// Ends a schedule permanently (terminal): it will never generate again and cannot be resumed. Existing
        /// invoices are untouched. Clears NextRunDate and the attention flag. Audits the end and optional reason.
        /// </summary>
        void EndSchedule(int scheduleId, string actingUser, string? reason);
    }
}
