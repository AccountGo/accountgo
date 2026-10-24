#nullable enable
using System;
using System.Collections.Generic;

namespace Api.Mcp.Models
{
    public class ChartOfAccountsMcpResponse
    {
        public string Organization { get; set; } = string.Empty;
        public int TotalAccounts { get; set; }
        public int TotalUnfilteredAccounts { get; set; }
        public DateTime AsOfDateUtc { get; set; } = DateTime.UtcNow;
        public Dictionary<string, int> ClassificationCounts { get; set; } = new();
        public List<AccountMcpSummary> Accounts { get; set; } = new();
    }

    public class AccountMcpSummary
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string AccountClass { get; set; } = string.Empty;
        public string NormalBalance { get; set; } = "Dr";
        public decimal Balance { get; set; }
        public decimal DebitBalance { get; set; }
        public decimal CreditBalance { get; set; }
        public bool IsCash { get; set; }
        public bool IsContraAccount { get; set; }
        public string? ParentAccountCode { get; set; }
        public bool CanPost { get; set; }
    }
}
