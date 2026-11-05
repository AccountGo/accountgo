using System;

namespace Services.Financial;

public sealed class LinkedEntryOperationException : InvalidOperationException
{
    public LinkedEntryOperationException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}
