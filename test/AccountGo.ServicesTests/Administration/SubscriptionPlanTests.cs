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
    public void GetPlanDefinitions_ServesRequiredModulesFromTheDomainRule()
    {
        var plans = _service.GetPlanDefinitions().ToDictionary(p => p.Key);

        Assert.Equal(Company.KnownPlans.OrderBy(p => p), plans.Keys.OrderBy(p => p));
        Assert.Equal(new[] { "sales", "purchases" }, plans["invoicing_free"].RequiredModules);
        Assert.Equal(new[] { "sales", "purchases" }, plans["invoicing_pro"].RequiredModules);
        Assert.Equal(new[] { "sales" }, plans["accounting_free"].RequiredModules);
        Assert.Equal(new[] { "sales" }, plans["accounting_pro"].RequiredModules);
        Assert.Equal(new[] { "sales" }, plans["enterprise"].RequiredModules);
        foreach (var plan in plans.Values)
            Assert.All(plan.RequiredModules, m => Assert.Contains(m, plan.IncludedModules));
    }
}
