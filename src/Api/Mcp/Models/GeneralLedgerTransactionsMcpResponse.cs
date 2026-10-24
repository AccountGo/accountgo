#nullable enable
using System;
using System.Collections.Generic;
using Dto.Financial;

namespace Api.Mcp.Models
{
    public class GeneralLedgerTransactionsMcpResponse
    {
        public string Organization { get; set; } = string.Empty;
        public int TotalCount { get; set; }
        public int Skip { get; set; }
        public int Take { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? AccountCode { get; set; }
        public List<GeneralLedgerTransaction> Transactions { get; set; } = new();
    }
}
