// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Core.Domain.Recurring;

namespace Services.Recurring
{
    /// <summary>
    /// Thrown when a requested run date is not valid for a schedule: not on its calendar, outside its start/end
    /// window, past its occurrence limit, or the schedule has ended. The API maps this to a 400.
    /// </summary>
    public sealed class InvalidRunDateException : Exception
    {
        public InvalidRunDateException(string message) : base(message) { }
    }

    /// <summary>
    /// What happened when the generation service processed a single recurring occurrence.
    /// </summary>
    public enum RecurringGenerationOutcome
    {
        /// <summary>
        /// Nothing was changed because the occurrence could not be processed by this caller
        /// (not in a processable state, or the caller does not hold its lease). The occurrence
        /// is left for whoever owns it.
        /// </summary>
        Aborted = 0,

        /// <summary>A draft invoice was created. The schedule asked only for a draft.</summary>
        DraftCreated = 1,

        /// <summary>
        /// Auto-issue was requested but a downgradeable guard failed, so a draft was created and
        /// the reason recorded on the occurrence (<see cref="RecurringOccurrence.GuardFailureReason"/>).
        /// </summary>
        DraftCreatedGuardFailed = 2,

        /// <summary>The draft was created and issued (invoice posted to the ledger).</summary>
        Issued = 3,

        /// <summary>
        /// Processing failed closed. The occurrence is marked <see cref="RecurringOccurrenceStatus.Failed"/>
        /// with an error code; nothing was posted. Any draft that was already created is kept for a human to fix.
        /// </summary>
        Failed = 4,

        /// <summary>
        /// The occurrence had already produced a document (idempotent replay). No new document was created.
        /// </summary>
        AlreadyProcessed = 5,

        /// <summary>
        /// The occurrence was skipped by a person (#484): no document was created, a reason was recorded, and the
        /// series advanced by one without changing the calendar anchor.
        /// </summary>
        Skipped = 6,
    }

    /// <summary>
    /// Stable error codes recorded on a failed occurrence. Short and machine-readable; the human-facing
    /// detail goes in <see cref="RecurringOccurrence.ErrorMessage"/> / <see cref="RecurringOccurrence.GuardFailureReason"/>.
    /// </summary>
    public static class RecurringErrorCodes
    {
        /// <summary>A line's tax could not be resolved; never issue at zero tax because resolution failed.</summary>
        public const string TaxNotResolved = "TAX_NOT_RESOLVED";

        /// <summary>The posting date falls in a locked period or outside the allowed range (books start / fiscal year).</summary>
        public const string BooksLocked = "BOOKS_LOCKED";

        /// <summary>The schedule or its lines could not be turned into a valid invoice (e.g. a free-text line with no item).</summary>
        public const string InvalidSchedule = "INVALID_SCHEDULE";

        /// <summary>Posting the issued invoice to the ledger failed for a reason not covered by a more specific code.</summary>
        public const string PostFailed = "POST_FAILED";
    }

    /// <summary>
    /// Reasons an auto-issue was downgraded to a draft. These are not failures: a valid draft exists and a
    /// person can review and issue it.
    /// </summary>
    public static class RecurringGuardReasons
    {
        public const string NotEnabledForAutoIssue =
            "Auto-issue is not enabled on this schedule.";

        public const string AutomationActorNotAuthorized =
            "The user who enabled auto-issue is no longer authorized to post invoices.";

        public const string ServicePeriodCrossesMonth =
            "The service period and document date are not in the same calendar month.";

        public const string MultiMonthCadence =
            "Quarterly and yearly schedules bill a multi-month period and are not auto-issued.";

        public const string ClientNotActive =
            "The client is archived.";

        public const string StockTrackedLine =
            "The invoice includes a stock-tracked item.";

        public const string LeaseNotHeld =
            "This run does not hold the occurrence lease.";

        public const string CustomerArchivedSchedulePaused =
            "The customer is archived; the schedule was paused. Existing invoices are unchanged.";
    }

    /// <summary>The result of processing one occurrence.</summary>
    public sealed class RecurringGenerationResult    {
        public RecurringGenerationOutcome Outcome { get; init; }

        /// <summary>The processed occurrence id, when one was involved.</summary>
        public int OccurrenceId { get; init; }

        /// <summary>The generated sales invoice id, when a document was created.</summary>
        public int? SalesInvoiceId { get; init; }

        /// <summary>Error code when <see cref="Outcome"/> is <see cref="RecurringGenerationOutcome.Failed"/>.</summary>
        public string? ErrorCode { get; init; }

        /// <summary>Human-readable detail for a failure or a downgrade-to-draft.</summary>
        public string? Message { get; init; }

        public static RecurringGenerationResult Aborted(int occurrenceId, string message) => new()
        {
            Outcome = RecurringGenerationOutcome.Aborted,
            OccurrenceId = occurrenceId,
            Message = message,
        };

        public static RecurringGenerationResult AlreadyProcessed(int occurrenceId, int? salesInvoiceId) => new()
        {
            Outcome = RecurringGenerationOutcome.AlreadyProcessed,
            OccurrenceId = occurrenceId,
            SalesInvoiceId = salesInvoiceId,
        };

        public static RecurringGenerationResult Draft(int occurrenceId, int salesInvoiceId) => new()
        {
            Outcome = RecurringGenerationOutcome.DraftCreated,
            OccurrenceId = occurrenceId,
            SalesInvoiceId = salesInvoiceId,
        };

        public static RecurringGenerationResult DraftGuardFailed(int occurrenceId, int salesInvoiceId, string reason) => new()
        {
            Outcome = RecurringGenerationOutcome.DraftCreatedGuardFailed,
            OccurrenceId = occurrenceId,
            SalesInvoiceId = salesInvoiceId,
            Message = reason,
        };

        public static RecurringGenerationResult Issued(int occurrenceId, int salesInvoiceId) => new()
        {
            Outcome = RecurringGenerationOutcome.Issued,
            OccurrenceId = occurrenceId,
            SalesInvoiceId = salesInvoiceId,
        };

        public static RecurringGenerationResult Fail(int occurrenceId, int? salesInvoiceId, string errorCode, string message) => new()
        {
            Outcome = RecurringGenerationOutcome.Failed,
            OccurrenceId = occurrenceId,
            SalesInvoiceId = salesInvoiceId,
            ErrorCode = errorCode,
            Message = message,
        };

        public static RecurringGenerationResult Skipped(int occurrenceId, string reason) => new()
        {
            Outcome = RecurringGenerationOutcome.Skipped,
            OccurrenceId = occurrenceId,
            Message = reason,
        };
    }
}
