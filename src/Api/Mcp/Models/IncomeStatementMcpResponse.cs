#nullable enable
using System;
using System.Collections.Generic;

namespace Api.Mcp.Models
{
    public class IncomeStatementMcpResponse : FinancialMcpBaseResponse
    {
        public string AccountingBasis { get; set; } = "Accrual";
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal TotalExpense { get; set; }
        public decimal NetIncome { get; set; }
        public List<IncomeStatementItemMcp> Items { get; set; } = new();
    }

    public class IncomeStatementItemMcp
    {
        public int AccountId { get; set; }
        public bool IsExpense { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }
}
