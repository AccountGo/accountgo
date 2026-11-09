// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Entitlements;
using Core.Tenancy;
using Moq;
using Services.Billing;
using Services.Entitlements;
using Xunit;

namespace AccountGo.BackendTests.Entitlements;

public sealed class CommunityEntitlementServiceContractTests : EntitlementServiceContractTests
{
    protected override IEntitlementService CreateService(ContractHarness harness)
    {
        ApplyHarnessFreeLimits(harness);
        var tenant = harness.Tenant ?? new Mock<ITenantContext>(MockBehavior.Strict);
        return new CommunityEntitlementService(
            harness.Options,
            new CommunityPlanCatalog(),
            Repo(harness.Companies).Object,
            Repo(harness.Members).Object,
            Repo(harness.Invitations).Object,
            UsageRepo(harness.Usage).Object,
            Repo(harness.Banks).Object,
            tenant.Object,
            harness.Time);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("unknown_plan")]
    [InlineData("accounting_free")]
    [InlineData("invoicing")]
    [InlineData("enterprise")]
    public void AnyStoredPlanKey_YieldsAllModulesMinusOptOuts(string? plan)
    {
        var company = new Company
        {
            Id = 1,
            Name = "Co",
            Plan = plan,
            DisabledModulesJson = "[\"inventory\"]"
        };
        var svc = CreateService(new EntitlementsOptions(), company);
        var snap = svc.Get(1);

        var expectedPlan = plan switch
        {
            "accounting_free" => "accounting_free",
            "invoicing" => "invoicing_free",
            "enterprise" => "enterprise",
            _ => CommunityPlanCatalog.PlanKey
        };
        Assert.Equal(expectedPlan, snap.Plan);
        Assert.Equal(expectedPlan, snap.EffectivePlan);
        Assert.Equal(plan is "accounting_free" or "invoicing", snap.Limited);
        Assert.Empty(snap.Features);
        Assert.Empty(snap.AllowedTransitions);
        Assert.False(svc.Evaluate(1, new EntitlementAction.Module("sales")).UpgradeAvailable);
        Assert.Null(snap.Lifecycle);
        Assert.DoesNotContain("inventory", snap.Modules, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("sales", snap.Modules, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("financials", snap.Modules, StringComparer.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(snap.Version));
    }

    [Fact]
    public void InvoicingFree_HasQuotas_Enforces5SalesDocumentsAnd20Bills()
    {
        var company = new Company
        {
            Id = 1,
            Name = "Co",
            Plan = "invoicing_free",
            DisabledModulesJson = "[]"
        };
        var usage = new List<EntitlementUsage>();
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero));
        var harness = new ContractHarness
        {
            Companies = new List<Company> { company },
            Usage = usage,
            Time = time
        };
        var svc = CreateService(harness);
        var snap = svc.Get(1);

        Assert.True(snap.Limited);
        Assert.Equal("invoicing_free", snap.Plan);
        var salesLimit = snap.Usage.Single(u => u.Resource == MeteredResource.SalesDocuments).Limit;
        var billsLimit = snap.Usage.Single(u => u.Resource == MeteredResource.Bills).Limit;
        var seatsLimit = snap.Usage.Single(u => u.Resource == MeteredResource.Seats).Limit;
        Assert.Equal(5, salesLimit);
        Assert.Equal(20, billsLimit);
        Assert.Equal(1, seatsLimit);

        // Record 5 sales documents -> at quota limit
        for (var i = 0; i < 5; i++)
        {
            svc.EnsureCanCreate(1, MeteredResource.SalesDocuments);
            usage.Add(new EntitlementUsage
            {
                Id = i + 1,
                CompanyId = 1,
                Resource = MeteredResources.Key(MeteredResource.SalesDocuments),
                OccurredAt = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc)
            });
        }

        // 6th sales document must throw QuotaExceededException
        var salesEx = Assert.Throws<QuotaExceededException>(() => svc.EnsureCanCreate(1, MeteredResource.SalesDocuments));
        Assert.Equal(5, salesEx.Limit);
        Assert.Equal(5, salesEx.Used);
        Assert.Equal(MeteredResource.SalesDocuments, salesEx.Resource);
        Assert.True(salesEx.Monthly);

        // Record 20 bills -> at quota limit
        for (var i = 0; i < 20; i++)
        {
            svc.EnsureCanCreate(1, MeteredResource.Bills);
            usage.Add(new EntitlementUsage
            {
                Id = 100 + i,
                CompanyId = 1,
                Resource = MeteredResources.Key(MeteredResource.Bills),
                OccurredAt = new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc)
            });
        }

        // 21st bill must throw QuotaExceededException
        var billEx = Assert.Throws<QuotaExceededException>(() => svc.EnsureCanCreate(1, MeteredResource.Bills));
        Assert.Equal(20, billEx.Limit);
        Assert.Equal(20, billEx.Used);
        Assert.Equal(MeteredResource.Bills, billEx.Resource);
        Assert.True(billEx.Monthly);
    }

    [Fact]
    public void AccountingFree_HasQuotas_Enforces10SalesDocumentsAnd10Bills()
    {
        var company = new Company
        {
            Id = 1,
            Name = "Co",
            Plan = "accounting_free",
            DisabledModulesJson = "[]"
        };
        var usage = new List<EntitlementUsage>();
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero));
        var harness = new ContractHarness
        {
            Companies = new List<Company> { company },
            Usage = usage,
            Time = time
        };
        var svc = CreateService(harness);
        var snap = svc.Get(1);

        Assert.True(snap.Limited);
        Assert.Equal("accounting_free", snap.Plan);
        var salesLimit = snap.Usage.Single(u => u.Resource == MeteredResource.SalesDocuments).Limit;
        var billsLimit = snap.Usage.Single(u => u.Resource == MeteredResource.Bills).Limit;
        var seatsLimit = snap.Usage.Single(u => u.Resource == MeteredResource.Seats).Limit;
        var banksLimit = snap.Usage.Single(u => u.Resource == MeteredResource.BankAccounts).Limit;
        Assert.Equal(10, salesLimit);
        Assert.Equal(10, billsLimit);
        Assert.Equal(1, seatsLimit);
        Assert.Equal(2, banksLimit);

        // Record 10 sales documents -> at quota limit
        for (var i = 0; i < 10; i++)
        {
            svc.EnsureCanCreate(1, MeteredResource.SalesDocuments);
            usage.Add(new EntitlementUsage
            {
                Id = i + 1,
                CompanyId = 1,
                Resource = MeteredResources.Key(MeteredResource.SalesDocuments),
                OccurredAt = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc)
            });
        }

        // 11th sales document must throw QuotaExceededException
        var salesEx = Assert.Throws<QuotaExceededException>(() => svc.EnsureCanCreate(1, MeteredResource.SalesDocuments));
        Assert.Equal(10, salesEx.Limit);
        Assert.Equal(10, salesEx.Used);

        // Record 10 bills -> at quota limit
        for (var i = 0; i < 10; i++)
        {
            svc.EnsureCanCreate(1, MeteredResource.Bills);
            usage.Add(new EntitlementUsage
            {
                Id = 100 + i,
                CompanyId = 1,
                Resource = MeteredResources.Key(MeteredResource.Bills),
                OccurredAt = new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc)
            });
        }

        // 11th bill must throw QuotaExceededException
        var billEx = Assert.Throws<QuotaExceededException>(() => svc.EnsureCanCreate(1, MeteredResource.Bills));
        Assert.Equal(10, billEx.Limit);
        Assert.Equal(10, billEx.Used);
    }

    [Fact]
    public void CompTier_OverridesQuotas()
    {
        var company = new Company
        {
            Id = 1,
            Name = "Co",
            Plan = "invoicing_free",
            Tier = "comp",
            DisabledModulesJson = "[]"
        };
        var usage = new List<EntitlementUsage>();
        for (var i = 0; i < 20; i++)
        {
            usage.Add(new EntitlementUsage
            {
                Id = i + 1,
                CompanyId = 1,
                Resource = MeteredResources.Key(MeteredResource.SalesDocuments),
                OccurredAt = DateTime.UtcNow
            });
        }

        var svc = CreateService(new ContractHarness
        {
            Companies = new List<Company> { company },
            Usage = usage
        });

        var snap = svc.Get(1);
        Assert.False(snap.Limited);
        // Exceeding 5 documents does not throw because company is comped
        Assert.Null(Record.Exception(() => svc.EnsureCanCreate(1, MeteredResource.SalesDocuments)));
    }

    [Fact]
    public void RequiredModule_CannotBeOptedOut()
    {
        var company = new Company
        {
            Id = 1,
            Name = "Co",
            Plan = "community",
            DisabledModulesJson = "[\"sales\",\"inventory\"]"
        };
        var svc = CreateService(new EntitlementsOptions(), company);
        Assert.Contains("sales", svc.Get(1).Modules, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("inventory", svc.Get(1).Modules, StringComparer.OrdinalIgnoreCase);
    }

    private static Mock<IRepository<T>> Repo<T>(List<T> data) where T : BaseEntity
    {
        var mock = new Mock<IRepository<T>>();
        mock.Setup(r => r.Table).Returns(() => data.AsQueryable());
        mock.Setup(r => r.GetById(It.IsAny<object>())).Returns((object id) =>
            data.FirstOrDefault(e => e.Id.Equals(Convert.ToInt32(id)))!);
        return mock;
    }

    private static Mock<IRepository<EntitlementUsage>> UsageRepo(List<EntitlementUsage> data)
    {
        var mock = Repo(data);
        mock.Setup(r => r.Insert(It.IsAny<EntitlementUsage>())).Callback<EntitlementUsage>(u =>
        {
            u.Id = data.Count + 1;
            data.Add(u);
        });
        return mock;
    }
}
