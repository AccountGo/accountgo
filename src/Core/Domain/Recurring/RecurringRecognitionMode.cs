namespace Core.Domain.Recurring;

/// <summary>
/// When revenue/expense is recognized for a recurring document.
/// </summary>
public enum RecurringRecognitionMode
{
    /// <summary>Recognize on the document date (v1 default). Dr AR, Cr revenue; Dr expense, Cr AP.</summary>
    OnDocumentDate = 1,
    /// <summary>Defer recognition over the service period (reserved for Phase 2; not implemented in v1).</summary>
    DeferOverServicePeriod = 2
}
