namespace Dto.Dashboard
{
    public class DashboardKpiDto
    {
        public decimal MoneyInBank { get; set; }
        public decimal OutstandingReceivables { get; set; }
        public decimal TotalPayables { get; set; }
        public decimal TotalAssets { get; set; }
        public decimal TotalSalesYtd { get; set; }
        public decimal TotalExpensesYtd { get; set; }
        public decimal NetProfitYtd { get; set; }
        public decimal CashFlowYtd { get; set; }
    }
}
