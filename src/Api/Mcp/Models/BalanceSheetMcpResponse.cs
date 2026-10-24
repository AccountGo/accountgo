#nullable enable
using System;
using System.Collections.Generic;

namespace Api.Mcp.Models
{
    public class BalanceSheetMcpResponse
    {
        public string Organization { get; set; } = string.Empty;
        public string AccountingBasis { get; set; } = "Accrual";
        public DateTime? FromDate { get; set; }
        public DateTime? AsOfDate { get; set; }
        public decimal TotalAssets { get; set; }
        public decimal TotalLiabilities { get; set; }
        public decimal TotalEquity { get; set; }
        public decimal NetAssets { get; set; }
        public bool IsBalanced { get; set; }
        public List<BalanceSheetItemMcp> Items { get; set; } = new();
    }

    public class BalanceSheetItemMcp
    {
        public int AccountId { get; set; }
        public int AccountClassId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }
}
