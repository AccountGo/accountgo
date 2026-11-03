namespace Core.Domain.Recurring;

/// <summary>
/// What happens after a document is generated from a schedule.
/// </summary>
public enum RecurringAction
{
    /// <summary>Create a draft; a person reviews and issues/approves.</summary>
    CreateDraft = 1,
    /// <summary>Issue the invoice automatically if all guards pass.</summary>
    AutoIssue = 2,
    /// <summary>Approve the bill automatically if all guards pass.</summary>
    AutoApprove = 3
}
