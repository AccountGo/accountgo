using System.Collections.Generic;

namespace Dto.Dashboard
{
    public class DashboardKpiDto
    {
        /// <summary>Always the sum of <see cref="BankBalances"/> and <see cref="UnassignedCash"/>.</summary>
        public decimal MoneyInBank { get; set; }

        /// <summary>
        /// Active bank accounts, plus archived ones that still hold a balance, with their book balance.
        /// Null when the caller cannot view bank accounts.
        /// </summary>
        public List<DashboardBankBalanceDto>? BankBalances { get; set; }

        /// <summary>Balance of cash accounts that no bank record is attached to.</summary>
        public decimal UnassignedCash { get; set; }
        /// <summary>
        /// Gross open receivables: sum of issued invoice <c>BalanceDue()</c> (lines after discount + tax + shipping − allocations).
        /// Same basis as <see cref="OverdueReceivables"/>. Zero when sales is not visible.
        /// </summary>
        public decimal OutstandingReceivables { get; set; }
        public decimal TotalPayables { get; set; }
        public decimal TotalAssets { get; set; }

        /// <summary>
        /// On the invoicing surface: issued invoice grand totals (tax + shipping, excluding drafts/voids) in range — not cash collected or profit.
        /// On accounting: income-statement revenue for the range.
        /// </summary>
        public decimal TotalSalesYtd { get; set; }
        public decimal EarnedIncomeYtd { get; set; }
        public decimal MoneyFromFreelanceWorkYtd { get; set; }
        public decimal DrawingsYtd { get; set; }
        public decimal TotalExpensesYtd { get; set; }
        public decimal NetProfitYtd { get; set; }
        public decimal CashFlowYtd { get; set; }

        /// <summary>Open sales invoices past due, using the same <c>BalanceDue()</c> basis as outstanding receivables.</summary>
        public decimal OverdueReceivables { get; set; }

        /// <summary>Count of open sales invoices still owed (excludes drafts and voids).</summary>
        public int OpenInvoiceCount { get; set; }

        /// <summary>Count of open sales invoices past their due date.</summary>
        public int OverdueInvoiceCount { get; set; }
    }

    public class DashboardBankBalanceDto
    {
        public int BankId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Balance { get; set; }
        /// <summary>True for an archived bank that still holds a balance.</summary>
        public bool IsArchived { get; set; }
    }
}
