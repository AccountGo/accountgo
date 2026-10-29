using System.Collections.Generic;

namespace Dto.Dashboard
{
    public class DashboardKpiDto
    {
        /// <summary>Always the sum of <see cref="BankBalances"/> and <see cref="UnassignedCash"/>.</summary>
        public decimal MoneyInBank { get; set; }

        /// <summary>Active bank accounts with their book balance. Null when the caller cannot view bank accounts.</summary>
        public List<DashboardBankBalanceDto>? BankBalances { get; set; }

        /// <summary>Balance of cash accounts that no bank record is attached to.</summary>
        public decimal UnassignedCash { get; set; }
        public decimal OutstandingReceivables { get; set; }
        public decimal TotalPayables { get; set; }
        public decimal TotalAssets { get; set; }
        public decimal TotalSalesYtd { get; set; }
        public decimal TotalExpensesYtd { get; set; }
        public decimal NetProfitYtd { get; set; }
        public decimal CashFlowYtd { get; set; }
    }

    public class DashboardBankBalanceDto
    {
        public int BankId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Balance { get; set; }
    }
}
