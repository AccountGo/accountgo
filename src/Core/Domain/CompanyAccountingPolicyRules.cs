using System;
using System.Collections.Generic;

namespace Core.Domain
{
    /// <summary>Validates framework, accounting-basis, and inventory-costing policy combinations.</summary>
    public static class CompanyAccountingPolicyRules
    {
        /// <summary>Returns validation errors for the full create-time policy, including tax regime, fiscal year and books start date.</summary>
        public static IReadOnlyList<string> Validate(CompanyProvisionOptions options, DateTime today)
        {
            var errors = new List<string>(Validate(options.ReportingFramework, options.AccountingBasis, options.InventoryCostingMethod));

            if (!Enum.IsDefined(typeof(TaxRegime), options.TaxRegime))
                errors.Add("Tax regime is not supported.");
            if (!Enum.IsDefined(typeof(IndustryTemplate), options.IndustryTemplate))
                errors.Add("Industry template is not supported.");

            if (!Enum.IsDefined(typeof(FiscalYearPattern), options.FiscalYearPattern))
            {
                errors.Add("Fiscal year pattern is not supported.");
            }
            else if (options.FiscalYearPattern == FiscalYearPattern.Custom
                && (options.FiscalYearStartMonth == null || options.FiscalYearStartMonth < 1 || options.FiscalYearStartMonth > 12))
            {
                errors.Add("Custom fiscal year requires a start month from 1 to 12.");
            }

            if (options.BooksStartDate is DateTime booksStart
                && (booksStart.Year < 1900 || booksStart.Date > today.Date.AddYears(1)))
            {
                errors.Add("Books start date must be between 1900 and one year from today.");
            }

            return errors;
        }

        /// <summary>Returns policy validation errors for the supplied company accounting choices.</summary>
        public static IReadOnlyList<string> Validate(
            ReportingFramework reportingFramework,
            AccountingBasis accountingBasis,
            InventoryCostingMethod inventoryCostingMethod)
        {
            var errors = new List<string>();
            var validFramework = Enum.IsDefined(typeof(ReportingFramework), reportingFramework);
            var validBasis = Enum.IsDefined(typeof(AccountingBasis), accountingBasis);

            if (!validFramework)
            {
                errors.Add("Reporting framework is not supported.");
            }

            if (!validBasis)
            {
                errors.Add("Accounting basis is not supported.");
            }
            else if (validFramework
                && (reportingFramework == ReportingFramework.CashBasis) != (accountingBasis == AccountingBasis.Cash))
            {
                errors.Add("Cash basis framework must use Cash accounting basis, and all other frameworks must use Accrual.");
            }

            if (!Enum.IsDefined(typeof(InventoryCostingMethod), inventoryCostingMethod))
            {
                errors.Add("Inventory costing method is not supported.");
            }

            return errors;
        }
    }
}
