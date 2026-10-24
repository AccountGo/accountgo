#nullable enable
using System;
using System.Collections.Generic;

namespace Api.Mcp.Models
{
    public class TrialBalanceMcpResponse
    {
        public string Organization { get; set; } = string.Empty;
        public string AccountingBasis { get; set; } = "Accrual";
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public bool IsBalanced { get; set; }
        public decimal Difference { get; set; }
        public int AccountCount { get; set; }
        public List<TrialBalanceItemMcp> Items { get; set; } = new();
    }

    public class TrialBalanceItemMcp
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }
}
