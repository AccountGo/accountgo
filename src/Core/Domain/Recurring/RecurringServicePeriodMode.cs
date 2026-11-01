namespace Core.Domain.Recurring;

/// <summary>
/// Which month the service period falls in relative to the document date.
/// </summary>
public enum RecurringServicePeriodMode
{
    /// <summary>Service period is the calendar month of the document date.</summary>
    CurrentMonth = 1,
    /// <summary>Service period is the previous calendar month (e.g., March bill for February service).</summary>
    PreviousMonth = 2
}
