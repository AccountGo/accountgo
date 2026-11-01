#nullable enable
using System;
using System.Collections.Generic;

namespace Core.Domain.Recurring;

/// <summary>
/// A template that generates recurring invoices or bills on a calendar schedule.
/// One occurrence per period. Not a second ledger; each run creates an ordinary document.
/// </summary>
public partial class RecurringSchedule : BaseEntity, ICompanyOwned
{
    public RecurringSchedule()
    {
        Lines = new HashSet<RecurringScheduleLine>();
        Occurrences = new HashSet<RecurringOccurrence>();
    }

    /// <summary>Tenant scope.</summary>
    public int CompanyId { get; set; }

    /// <summary>Whether this generates sales invoices or vendor bills.</summary>
    public RecurringScheduleKind Kind { get; set; }

    /// <summary>Customer ID (for sales invoices) or Vendor ID (for vendor bills).</summary>
    public int PartyId { get; set; }

    /// <summary>Descriptive name for the schedule (e.g., "Monthly Retainer - Acme Corp").</summary>
    public string Name { get; set; } = "";

    /// <summary>Current state: Active generates documents; Paused and Ended do not.</summary>
    public RecurringScheduleStatus Status { get; set; } = RecurringScheduleStatus.Active;

    /// <summary>How often to run: weekly, monthly, quarterly, yearly.</summary>
    public RecurringCadence Cadence { get; set; }

    private int _interval = 1;
    
    /// <summary>Interval multiplier (e.g., 2 for every 2 months). Minimum 1. Default 1.</summary>
    public int Interval
    {
        get => _interval;
        set => _interval = value < 1 ? 1 : value;
    }

    /// <summary>
    /// Day of month for monthly/quarterly/yearly cadence (1-31).
    /// Day 31 means: 31 Jan, 28/29 Feb, 31 Mar — last day of month when the month has fewer days.
    /// Required for non-weekly cadences.
    /// </summary>
    public int? DayOfMonth { get; set; }

    /// <summary>Date of the first occurrence. Time component is ignored; timezone comes from CompanySetting.</summary>
    public DateTime StartDate { get; set; }

    /// <summary>Optional end date; no occurrences after this date.</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>Optional maximum number of occurrences; the schedule ends after this many runs.</summary>
    public int? MaxOccurrences { get; set; }

    /// <summary>The next date a run should occur. Computed from the calendar rules, not from "now".</summary>
    public DateTime? NextRunDate { get; set; }

    /// <summary>How many occurrences have been generated (including drafts, issued, failed, skipped).</summary>
    public int OccurrenceCount { get; set; } = 0;

    /// <summary>What to do after generating: create draft, auto-issue, or auto-approve.</summary>
    public RecurringAction Action { get; set; } = RecurringAction.CreateDraft;

    /// <summary>How revenue/expense is recognized. V1 only supports OnDocumentDate.</summary>
    public RecurringRecognitionMode RecognitionMode { get; set; } = RecurringRecognitionMode.OnDocumentDate;

    /// <summary>Which period the run bills: the current cadence period or the one immediately before it. See <see cref="RecurringServicePeriodMode"/>.</summary>
    public RecurringServicePeriodMode ServicePeriodMode { get; set; } = RecurringServicePeriodMode.CurrentMonth;

    /// <summary>Payment terms to copy to generated documents.</summary>
    public int? PaymentTermId { get; set; }

    /// <summary>Notes to copy to generated documents.</summary>
    public string? Notes { get; set; }

    /// <summary>Payment instructions (for invoices) to copy to generated documents.</summary>
    public string? PaymentInstructions { get; set; }

    /// <summary>For bills: a pattern to generate vendor invoice numbers (e.g., "RECUR-{YYYY}-{MM}-{NNN}").</summary>
    public string? VendorInvoiceNumberPattern { get; set; }

    /// <summary>The user who turned on auto-issue or auto-approve; shown as the actor on generated documents.</summary>
    public string? AutomationEnabledBy { get; set; }

    /// <summary>When automation was enabled.</summary>
    public DateTime? AutomationEnabledAt { get; set; }

    /// <summary>Count of consecutive failed occurrences; used for attention inbox.</summary>
    public int ConsecutiveFailures { get; set; } = 0;

    /// <summary>Whether this schedule needs attention (failed, held, or send-failed occurrences).</summary>
    public bool NeedsAttention { get; set; } = false;

    // Navigation properties
    public virtual Company Company { get; set; } = null!;
    public virtual ICollection<RecurringScheduleLine> Lines { get; set; }
    public virtual ICollection<RecurringOccurrence> Occurrences { get; set; }
}
