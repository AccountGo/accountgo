namespace Dto.Dashboard
{
    /// <summary>
    /// Domains the caller is authorized to see on the dashboard. The UI must gate cards and
    /// combined figures from this, not from plan modules alone.
    /// </summary>
    public class DashboardCapabilitiesDto
    {
        public bool Financials { get; set; }
        public bool Sales { get; set; }
        public bool Purchases { get; set; }
        public bool Banking { get; set; }

        /// <summary>True when cash/bank totals were computed from an authorized source.</summary>
        public bool MoneyInBank { get; set; }

        /// <summary>True when open receivables were computed from authorized sales data.</summary>
        public bool Receivables { get; set; }

        /// <summary>True when open payables were computed from authorized purchases data.</summary>
        public bool Payables { get; set; }
    }
}
