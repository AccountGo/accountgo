namespace Core.Domain
{
    /// <summary>Chart-of-accounts template ids. Every id currently seeds the same account set; the id is stored so variants can be introduced later.</summary>
    public static class CoaTemplates
    {
        public const string Legacy = "legacy-default";

        public static string Resolve(IndustryTemplate industry, ReportingFramework framework)
        {
            var industryPart = industry == IndustryTemplate.ProductRetail ? "product-retail"
                : industry == IndustryTemplate.Mixed ? "mixed" : "services";
            var frameworkPart = framework == ReportingFramework.UsGaapPrivate ? "us-gaap"
                : framework == ReportingFramework.Ifrs ? "ifrs"
                : framework == ReportingFramework.CashBasis ? "cash" : "ifrs-smes";
            return industryPart + "-" + frameworkPart;
        }
    }
}
