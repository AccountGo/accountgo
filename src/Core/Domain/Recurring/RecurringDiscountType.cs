namespace Core.Domain.Recurring;

/// <summary>
/// How a discount value should be interpreted on a recurring schedule line.
/// </summary>
public enum RecurringDiscountType
{
    /// <summary>Discount is a percentage (e.g., 10 means 10% off).</summary>
    Percentage = 0,
    
    /// <summary>Discount is an absolute amount in the schedule's currency.</summary>
    Amount = 1
}
