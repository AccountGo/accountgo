#nullable enable
using System;
using System.Collections.Generic;

namespace Dto.Financial
{
    /// <summary>The outcome of the books review checks for one period.</summary>
    public class BooksChecksResult
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public List<BooksCheck> Checks { get; set; } = new();
    }

    /// <summary>One check. Status is Pass, Finding (a difference or error), Review (items a person should look at) or NotChecked.</summary>
    public class BooksCheck
    {
        public string Id { get; set; } = string.Empty;
        /// <summary>ledger, sales, purchases, journals or bank.</summary>
        public string Area { get; set; } = string.Empty;
        /// <summary>The question the check answers: real, missing, amount, period, account or control.</summary>
        public string Question { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = BooksCheckStatus.Pass;
        /// <summary>How many records the check flagged.</summary>
        public int Count { get; set; }
        /// <summary>The difference or the total of the flagged records, when the check has an amount.</summary>
        public decimal? Amount { get; set; }
        public string? Detail { get; set; }
        /// <summary>Up to ten flagged records. Path is relative to the workspace, e.g. /sales/12.</summary>
        public List<BooksCheckExample> Examples { get; set; } = new();
    }

    public class BooksCheckExample
    {
        public string Label { get; set; } = string.Empty;
        public string? Path { get; set; }
        public DateTime? Date { get; set; }
        public decimal? Amount { get; set; }
    }

    public static class BooksCheckStatus
    {
        public const string Pass = "Pass";
        public const string Finding = "Finding";
        public const string Review = "Review";
        public const string NotChecked = "NotChecked";
    }
}
