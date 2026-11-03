namespace Core.Domain.Recurring;

/// <summary>
/// Whether a schedule is producing new documents.
/// </summary>
public enum RecurringScheduleStatus
{
    /// <summary>The schedule generates documents on each run date.</summary>
    Active = 1,
    /// <summary>Temporarily stopped; existing documents remain. Resume continues from the next date.</summary>
    Paused = 2,
    /// <summary>Permanently stopped; no more occurrences will be created.</summary>
    Ended = 3
}
