using System.Collections.Generic;
using Dto.Banking;

namespace Dto.Dashboard
{
    public class DashboardSummaryDto
    {
        /// <summary>
        /// <c>Accounting</c> when the caller can see the ledger; otherwise <c>Invoicing</c>
        /// (sales/purchases surface without P&amp;L / balance sheet).
        /// </summary>
        public string Surface { get; set; } = "Invoicing";

        /// <summary>Authorized domains and which KPI groups were actually computed.</summary>
        public DashboardCapabilitiesDto Capabilities { get; set; } = new();

        public DashboardKpiDto Kpis { get; set; } = new();
        public DashboardBalanceSnapshotDto BalanceSnapshot { get; set; } = new();
        public List<DashboardTrendPointDto> MonthlySales { get; set; } = new();
        public List<DashboardTrendPointDto> MonthlyExpenses { get; set; } = new();
        public List<DashboardReceivableItemDto> TopReceivables { get; set; } = new();
        public List<DashboardPayableItemDto> UpcomingPayables { get; set; } = new();
        public List<DashboardActivityItemDto> RecentActivity { get; set; } = new();

        /// <summary>Null unless bank accounts are visible to the caller.</summary>
        public BankCloseStatusDto? BankCloseStatus { get; set; }
    }
}
