using System;
using Dto.Dashboard;

namespace Services.Dashboard
{
    public interface IDashboardService
    {
        DashboardSummaryDto GetDashboardSummary(
            DateTime? from = null,
            DateTime? to = null,
            int topReceivables = 5,
            int topPayables = 5,
            int recentActivity = 5);
    }
}
