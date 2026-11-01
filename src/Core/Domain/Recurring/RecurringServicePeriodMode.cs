namespace Core.Domain.Recurring;

/// <summary>
/// Which period a run bills relative to its document date. Despite the member names (kept for storage
/// compatibility), the meaning is generalized across cadences: the period is the cadence's natural span
/// (a week, a month, a quarter, a year), and the mode chooses the current span or the one immediately before it.
/// </summary>
public enum RecurringServicePeriodMode
{
    /// <summary>The current cadence period containing the document date (e.g. this calendar month for monthly).</summary>
    CurrentMonth = 1,
    /// <summary>The period immediately before the current one — one whole cadence span earlier (e.g. the previous month for monthly, the previous quarter for quarterly).</summary>
    PreviousMonth = 2
}
