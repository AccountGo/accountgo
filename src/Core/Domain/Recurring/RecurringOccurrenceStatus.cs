namespace Core.Domain.Recurring;

/// <summary>
/// The state of a single run of a recurring schedule.
/// </summary>
public enum RecurringOccurrenceStatus
{
    /// <summary>Waiting for the run date.</summary>
    Scheduled = 1,
    /// <summary>A worker has leased this occurrence and is processing it.</summary>
    Running = 2,
    /// <summary>A draft document was created; not yet issued/approved.</summary>
    DraftCreated = 3,
    /// <summary>The document was issued (invoice) or approved (bill).</summary>
    Issued = 4,
    /// <summary>Skipped by a person; does not move later dates.</summary>
    Skipped = 5,
    /// <summary>Processing failed; see error message.</summary>
    Failed = 6,
    /// <summary>More than 3 periods behind; waiting for manual release.</summary>
    Held = 7,
    /// <summary>Replaced by a later regeneration; kept for history.</summary>
    Superseded = 8
}
