#nullable enable
using System;
using System.Collections.Generic;

namespace Api.Mcp.Models
{
    public class CashFlowStatementMcpResponse : FinancialMcpBaseResponse
    {
        public string AccountingBasis { get; set; } = "Accrual";
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public decimal NetIncome { get; set; }
        public decimal OperatingCashFlow { get; set; }
        public decimal InvestingCashFlow { get; set; }
        public decimal FinancingCashFlow { get; set; }
        public decimal NetChangeInCash { get; set; }
        public decimal StartingCash { get; set; }
        public decimal EndingCash { get; set; }
        public bool IsBalanced { get; set; }
        public List<CashFlowLineMcp> OperatingActivities { get; set; } = new();
        public List<CashFlowLineMcp> InvestingActivities { get; set; } = new();
        public List<CashFlowLineMcp> FinancingActivities { get; set; } = new();
    }

    public class CashFlowLineMcp
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string LineType { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }
}
