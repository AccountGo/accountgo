using AutoMapper;
using Core.Data;
using Core.Domain;
using Core.Domain.Auditing;
using Core.Domain.Financials;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.TaxSystem;
using Core.Tenancy;
using Dto.Subscription;
using Moq;
using Services.Administration;
using Services.Billing;
using Services.Security;

namespace AccountGo.ServicesTests.Administration;

/// <summary>The server is the only source of plan rules: unknown plans are rejected, not silently mapped.</summary>
public class SubscriptionPlanTests
{
    private readonly Company _company = new() { Id = 7, Name = "Acme", Slug = "acme", Plan = "invoicing_free" };
    private readonly Mock<IRepository<Company>> _companyRepo = new();
    private readonly AdministrationService _service;

    public SubscriptionPlanTests()
    {
        _companyRepo.Setup(r => r.GetById(It.IsAny<object>())).Returns(_company);
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.RequireCompanyId()).Returns(_company.Id);
        tenant.SetupGet(t => t.CompanyId).Returns(_company.Id);

        _service = new AdministrationService(
            Mock.Of<IRepository<FinancialYear>>(),
            Mock.Of<IRepository<TaxGroup>>(),
            Mock.Of<IRepository<ItemTaxGroup>>(),
            Mock.Of<IRepository<PaymentTerm>>(),
            Mock.Of<IRepository<Bank>>(),
            Mock.Of<IRepository<Tax>>(),
            Mock.Of<IRepository<GeneralLedgerSetting>>(),
            Mock.Of<IRepository<Account>>(),
            Mock.Of<IRepository<AuditLog>>(),
            Mock.Of<IRepository<SalesInvoiceLine>>(),
            Mock.Of<IRepository<PurchaseInvoiceLine>>(),
            Mock.Of<ISecurityRepository>(),
            Mock.Of<IMapper>(),
            _companyRepo.Object,
            tenant.Object);
    }

    [Theory]
    [InlineData("foo")]
    [InlineData("pro")]
    [InlineData("starter")]
    public void UpdateSubscription_UnknownPlan_IsRejectedAndNotSaved(string plan)
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.UpdateSubscription(new UpdateSubscriptionDto { Plan = plan }));

        Assert.Contains(plan, ex.Message);
        Assert.Equal("invoicing_free", _company.Plan);
        _companyRepo.Verify(r => r.Update(It.IsAny<Company>()), Times.Never);
    }

    [Theory]
    [InlineData("invoicing_free")]
    [InlineData("invoicing_pro")]
    [InlineData("accounting_free")]
    [InlineData("accounting_pro")]
    public void UpdateSubscription_ProductTierPlan_IsAccepted(string plan)
    {
        var result = _service.UpdateSubscription(new UpdateSubscriptionDto { Plan = plan });

        Assert.Equal(plan, _company.Plan);
        Assert.Equal(Company.ModulesForPlan(plan), result.ActiveModules);
    }

    [Fact]
    public void UpdateSubscription_KnownPlan_IsNormalizedAndResetsModules()
    {
        _company.ActiveModulesJson = "[\"sales\"]";

        var result = _service.UpdateSubscription(new UpdateSubscriptionDto { Plan = "  Accounting " });

        Assert.Equal("accounting_free", _company.Plan);
        Assert.Null(_company.ActiveModulesJson);
        Assert.Equal("[]", _company.DisabledModulesJson);
        Assert.Equal(Company.ModulesForPlan("accounting_free"), result.ActiveModules);
    }

    [Fact]
    public void UpdateSubscription_NoPlan_KeepsCurrentPlanAndAppliesModules()
    {
        _company.Plan = "accounting_free";

        var result = _service.UpdateSubscription(new UpdateSubscriptionDto { ActiveModules = new List<string> { "sales", "reports" } });

        Assert.Equal("accounting_free", _company.Plan);
        Assert.Equal(new[] { "sales", "reports" }, result.ActiveModules);
        Assert.Contains("financials", _company.DisabledModulesJson!);
    }

    [Fact]
    public void CommunityPlanCatalog_ServesOneUnpricedPlanWithEveryModule()
    {
        var plan = Assert.Single(new CommunityPlanCatalog().GetPlans());

        Assert.Equal("community", plan.Key);
        Assert.Equal("Community", plan.Name);
        Assert.Null(plan.MonthlyPrice);
        Assert.False(plan.SelfService);
        Assert.Equal(_service.GetModuleDefinitions().Select(m => m.Key), plan.IncludedModules);
        Assert.Equal(new[] { "sales" }, plan.RequiredModules);
    }
}
