using System;
using System.Collections.Generic;

namespace Core.Domain
{
    /// <summary>Validates framework, accounting-basis, and inventory-costing policy combinations.</summary>
    public static class CompanyAccountingPolicyRules
    {
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
