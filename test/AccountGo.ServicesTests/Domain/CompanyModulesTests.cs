using Core.Domain;
using Xunit;

namespace AccountGo.ServicesTests.Domain;

/// <summary>
/// Invoicing includes sales + purchases so freelancers can bill clients and track expenses
/// without unlocking full accounting (GL / reports).
/// </summary>
public class CompanyModulesTests
{
    [Fact]
    public void ModulesForPlan_Invoicing_IncludesSalesAndPurchases()
    {
        var modules = Company.ModulesForPlan("invoicing");

        Assert.Equal(new[] { "sales", "purchases" }, modules);
    }

    [Fact]
    public void ModulesForPlan_Accounting_Unchanged()
    {
        var modules = Company.ModulesForPlan("accounting");

        Assert.Equal(new[] { "sales", "financials", "purchases", "reports" }, modules);
    }

    [Fact]
    public void GetActiveModules_InvoicingWithLegacySalesOnlyJson_IncludesPurchases()
    {
        var company = new Company
        {
            Plan = "invoicing",
            ActiveModulesJson = "[\"sales\"]",
        };

        var modules = company.GetActiveModules();

        Assert.Contains("sales", modules);
        Assert.Contains("purchases", modules);
    }

    [Fact]
    public void GetActiveModules_InvoicingWithNullJson_ReturnsPlanDefaults()
    {
        var company = new Company { Plan = "invoicing", ActiveModulesJson = null };

        Assert.Equal(Company.ModulesForPlan("invoicing"), company.GetActiveModules());
    }

    [Fact]
    public void HasModule_InvoicingLegacySalesOnly_PurchasesEnabled()
    {
        var company = new Company
        {
            Plan = "invoicing",
            ActiveModulesJson = "[\"sales\"]",
        };

        Assert.True(company.HasModule("purchases"));
    }

    [Fact]
    public void GetActiveModules_AccountingCanOmitPurchasesWhenStored()
    {
        // Accounting still allows turning purchases off; only invoicing forces it on.
        var company = new Company
        {
            Plan = "accounting",
            ActiveModulesJson = "[\"sales\",\"financials\",\"reports\"]",
        };

        var modules = company.GetActiveModules();

        Assert.DoesNotContain("purchases", modules);
        Assert.Contains("sales", modules);
    }
}
