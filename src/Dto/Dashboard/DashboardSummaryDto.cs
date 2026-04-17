using System.Collections.Generic;

namespace Dto.Dashboard
{
    public class DashboardSummaryDto
    {
        public DashboardKpiDto Kpis { get; set; } = new();
        public DashboardBalanceSnapshotDto BalanceSnapshot { get; set; } = new();
        public List<DashboardTrendPointDto> MonthlySales { get; set; } = new();
        public List<DashboardTrendPointDto> MonthlyExpenses { get; set; } = new();
        public List<DashboardReceivableItemDto> TopReceivables { get; set; } = new();
        public List<DashboardPayableItemDto> UpcomingPayables { get; set; } = new();
        public List<DashboardActivityItemDto> RecentActivity { get; set; } = new();
    }
}
