namespace Core.Domain.Recurring;

/// <summary>
/// How often a recurring schedule runs.
/// </summary>
public enum RecurringCadence
{
    /// <summary>Every N weeks on the same weekday.</summary>
    Weekly = 1,
    /// <summary>Every N months on the same day of month.</summary>
    Monthly = 2,
    /// <summary>Every 3 months on the same day of month.</summary>
    Quarterly = 3,
    /// <summary>Every 12 months on the same day of month.</summary>
    Yearly = 4
}
