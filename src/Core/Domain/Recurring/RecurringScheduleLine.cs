#nullable enable
using System;

namespace Core.Domain.Recurring;

/// <summary>
/// A line item on a recurring schedule, copied to each generated invoice or bill.
/// </summary>
public partial class RecurringScheduleLine : BaseEntity, ICompanyOwned
{
    /// <summary>Tenant scope.</summary>
    public int CompanyId { get; set; }

    /// <summary>The schedule this line belongs to.</summary>
    public int RecurringScheduleId { get; set; }

    /// <summary>Line item description; copied to the generated document line.</summary>
    public string? Description { get; set; }

    /// <summary>Item being sold or purchased; null for free-text lines.</summary>
    public int? ItemId { get; set; }

    /// <summary>Quantity; default 1.</summary>
    public decimal Quantity { get; set; } = 1;

    /// <summary>Unit price or amount.</summary>
    public decimal Amount { get; set; }

    /// <summary>Discount value. Interpretation depends on DiscountType.</summary>
    public decimal Discount { get; set; }

    /// <summary>Whether Discount is a percentage (default) or an absolute amount.</summary>
    public RecurringDiscountType DiscountType { get; set; } = RecurringDiscountType.Percentage;

    /// <summary>Tax code to apply; null for no tax.</summary>
    public int? TaxId { get; set; }

    /// <summary>GL account for the line (for bills); for invoices, the sales account from item is used.</summary>
    public int? AccountId { get; set; }

    /// <summary>Sort order for display.</summary>
    public int SortOrder { get; set; } = 0;

    // Navigation properties
    public virtual RecurringSchedule Schedule { get; set; } = null!;
    public virtual Items.Item? Item { get; set; }
    public virtual TaxSystem.Tax? Tax { get; set; }
    public virtual Financials.Account? Account { get; set; }
}
