using System;
using System.Collections.Generic;

namespace Core.Domain
{
    /// <summary>
    /// Chart-of-accounts template ids. All templates are subsets of the single base chart (<c>coa.csv</c>): a template
    /// lists the base accounts it leaves out. Inventory costing method is metadata only and does not change the accounts.
    /// </summary>
    public static class CoaTemplates
    {
        public const string Legacy = "legacy-default";
        public const string ServicesSme = "services-sme";
        public const string TradingWeightedAverage = "trading-wa";
        public const string TradingFifo = "trading-fifo";
        public const string MixedSme = "mixed-sme";

        /// <summary>Accounts every template must keep because <c>GeneralLedgerSetting</c> points at them.</summary>
        public static readonly IReadOnlyList<string> RequiredControlAccountCodes = new[] { "20110", "10810", "40400", "40500" };

        // Manufacturing-style depth that a services business does not use: work in progress and purchase price variance.
        private static readonly HashSet<string> ServicesOmitted = new HashSet<string>(StringComparer.Ordinal) { "10900", "50500" };
        private static readonly HashSet<string> None = new HashSet<string>(StringComparer.Ordinal);

        public static string Resolve(IndustryTemplate industry, InventoryCostingMethod inventoryCosting)
        {
            switch (industry)
            {
                case IndustryTemplate.ProductRetail:
                    return inventoryCosting == InventoryCostingMethod.Fifo ? TradingFifo : TradingWeightedAverage;
                case IndustryTemplate.Mixed:
                    return MixedSme;
                default:
                    return ServicesSme;
            }
        }

        public static bool IsKnown(string templateId) =>
            templateId == Legacy || templateId == ServicesSme || templateId == TradingWeightedAverage
            || templateId == TradingFifo || templateId == MixedSme;

        /// <summary>Base-chart account codes the template leaves out.</summary>
        public static ISet<string> OmittedAccountCodes(string templateId) =>
            templateId == ServicesSme ? ServicesOmitted : None;
    }
}
