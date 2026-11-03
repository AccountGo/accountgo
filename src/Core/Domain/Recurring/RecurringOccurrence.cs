#nullable enable
using System;

namespace Core.Domain.Recurring;

/// <summary>
/// A single run of a recurring schedule, tracking the period, document, and status.
/// Uniquely identified by company + schedule + scheduled run date.
/// </summary>
public partial class RecurringOccurrence : BaseEntity, ICompanyOwned
{
    /// <summary>Tenant scope.</summary>
    public int CompanyId { get; set; }

    /// <summary>The schedule that owns this occurrence.</summary>
    public int RecurringScheduleId { get; set; }

    /// <summary>The calendar date this run was scheduled to occur. This is the idempotency key component.</summary>
    public DateTime ScheduledRunDate { get; set; }

    /// <summary>Start of the service period this occurrence covers.</summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>End of the service period this occurrence covers.</summary>
    public DateTime PeriodEnd { get; set; }

    /// <summary>The document date (invoice date or bill date).</summary>
    public DateTime DocumentDate { get; set; }

    /// <summary>The due date computed from payment terms.</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>Current state of this occurrence.</summary>
    public RecurringOccurrenceStatus Status { get; set; } = RecurringOccurrenceStatus.Scheduled;

    /// <summary>
    /// Idempotency key for deduplication. Format: {companyId}-{scheduleId}-{scheduledRunDate:yyyy-MM-dd}.
    /// </summary>
    public string IdempotencyKey { get; set; } = "";

    /// <summary>When this occurrence was leased by a worker; null when not leased.</summary>
    public DateTime? LeasedUntil { get; set; }

    /// <summary>Worker instance or process ID that holds the lease.</summary>
    public string? LeasedBy { get; set; }

    /// <summary>Number of processing attempts (for retry logic).</summary>
    public int AttemptCount { get; set; } = 0;

    /// <summary>Error code if Status is Failed (e.g., "TAX_NOT_RESOLVED", "BOOKS_LOCKED").</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Human-readable error message for failed occurrences.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>The generated sales invoice ID (for sales schedules).</summary>
    public int? SalesInvoiceId { get; set; }

    /// <summary>The generated vendor bill ID (for vendor bill schedules).</summary>
    public int? PurchaseInvoiceId { get; set; }

    /// <summary>When the occurrence was created (scheduled).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the occurrence was last updated (status change, document created, etc.).</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Why auto-issue or auto-approve did not happen (for drafts that needed review).</summary>
    public string? GuardFailureReason { get; set; }

    /// <summary>
    /// True once the owning schedule's <see cref="RecurringSchedule.NextRunDate"/> has been advanced past this
    /// occurrence. Set atomically in the same write that commits the occurrence's terminal state, so the series is
    /// advanced exactly once per occurrence even across crashes and concurrent/replayed runs. A terminal occurrence
    /// left with this still false is an interrupted run whose advancement a later run (or replay) completes.
    /// </summary>
    public bool SeriesAdvanced { get; set; }

    /// <summary>User who manually skipped or released this occurrence.</summary>
    public string? ActionBy { get; set; }

    /// <summary>Reason provided when skipping.</summary>
    public string? SkipReason { get; set; }

    // Navigation properties
    public virtual RecurringSchedule Schedule { get; set; } = null!;
    public virtual Sales.SalesInvoiceHeader? SalesInvoice { get; set; }
    public virtual Purchases.PurchaseInvoiceHeader? PurchaseInvoice { get; set; }
}
