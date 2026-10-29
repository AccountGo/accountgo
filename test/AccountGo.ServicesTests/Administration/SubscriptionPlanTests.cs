using System.Collections.Generic;
using System.Linq;
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
using Xunit;

namespace AccountGo.ServicesTests.Administration;

/// <summary>Community UpdateSubscription stores the resolved catalog key and uses catalog module allow-lists.</summary>
public class SubscriptionPlanTests
{
    private readonly Company _company = new() { Id = 7, Name = "Acme", Slug = "acme", Plan = "community" };
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
            new CommunityPlanCatalog(),
            _companyRepo.Object,
            tenant.Object);
    }

    [Theory]
    [InlineData("foo")]
    [InlineData("accounting_free")]
    [InlineData("invoicing")]
    public void UpdateSubscription_AnyPlanKey_StoresCommunity(string plan)
    {
        var result = _service.UpdateSubscription(new UpdateSubscriptionDto { Plan = plan });

        Assert.Equal(CommunityPlanCatalog.PlanKey, _company.Plan);
        Assert.Equal(CommunityPlanCatalog.PlanKey, result.Plan);
        Assert.Equal(CommunityPlanCatalog.AllModules, result.ActiveModules);
    }

    [Fact]
    public void UpdateSubscription_NoPlan_KeepsResolvedKeyAndAppliesModules()
    {
        _company.Plan = "legacy_key";

        var result = _service.UpdateSubscription(new UpdateSubscriptionDto
        {
            ActiveModules = new List<string> { "sales", "reports" }
        });

        Assert.Equal(CommunityPlanCatalog.PlanKey, _company.Plan);
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
