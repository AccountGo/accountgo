using System;

namespace Core.Domain
{
    /// <summary>Hard-gate accounting policy captured when a workspace is created. Defaults match legacy clients that send only name, slug, currency and plan.</summary>
    public class CompanyProvisionOptions
    {
        public string Currency { get; set; } = "USD";
        public string LegalName { get; set; }
        public ReportingFramework ReportingFramework { get; set; } = ReportingFramework.IfrsForSmes;
        public AccountingBasis AccountingBasis { get; set; } = AccountingBasis.Accrual;
        public FiscalYearPattern FiscalYearPattern { get; set; } = FiscalYearPattern.Calendar;
        public int? FiscalYearStartMonth { get; set; }
        /// <summary>When null the books start on the first day of the current calendar year, matching the fiscal year legacy clients received.</summary>
        public DateTime? BooksStartDate { get; set; }
        public TaxRegime TaxRegime { get; set; } = TaxRegime.None;
        public IndustryTemplate IndustryTemplate { get; set; } = IndustryTemplate.Services;
        public EntityType EntityType { get; set; } = EntityType.SoleProprietor;
        public InventoryCostingMethod InventoryCostingMethod { get; set; } = InventoryCostingMethod.None;
        public string CoaTemplateId { get; set; }
    }
}
