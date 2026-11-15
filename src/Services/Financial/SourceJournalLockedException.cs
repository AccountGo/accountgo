using System;

namespace Services.Financial;

public sealed class SourceJournalLockedException : InvalidOperationException
{
    public string Code { get; }

    public SourceJournalLockedException(string code, string message)
        : base(message)
    {
        Code = code;
    }
}
