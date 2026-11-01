namespace Core.Domain.Recurring;

/// <summary>
/// The type of document a recurring schedule generates.
/// </summary>
public enum RecurringScheduleKind
{
    /// <summary>Generates sales invoices.</summary>
    SalesInvoice = 1,
    /// <summary>Generates vendor bills.</summary>
    VendorBill = 2
}
