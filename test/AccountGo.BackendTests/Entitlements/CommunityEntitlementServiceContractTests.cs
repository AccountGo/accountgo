// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Entitlements;
using Core.Tenancy;
using Moq;
using Services.Entitlements;
using Xunit;

namespace AccountGo.BackendTests.Entitlements
{
    /// <summary>Community / self-host cases that hosted does not share (self-service-all, ResolveFeatures seam).</summary>
    public sealed class CommunityEntitlementServiceContractTests : EntitlementServiceContractTests
    {
        protected override void ApplyHarnessFreeLimits(ContractHarness harness)
        {
            foreach (var (resource, limit) in harness.FreeLimits)
                harness.Options.FreeLimits[resource] = limit;
        }

        protected override IEntitlementService CreateService(ContractHarness harness)
        {
            ApplyHarnessFreeLimits(harness);
            return Build(harness, featuresWhenEnabled: null);
        }

        protected override IEntitlementService CreateFeatureSeamService(
            ContractHarness harness, IReadOnlySet<string> featuresWhenEnabled)
        {
            ApplyHarnessFreeLimits(harness);
            return Build(harness, featuresWhenEnabled);
        }

        [Fact]
        public void SelfHost_EnabledFalse_Unlimited_AllPlansSelfService_SnapshotDisabled()
        {
            var company = FreeCompany();
            var svc = CreateService(new EntitlementsOptions { Enabled = false, FeaturesEnabled = true }, company);
            var snap = svc.Get(company.Id);

            Assert.False(snap.Enabled);
            Assert.False(snap.Limited);
            Assert.Empty(snap.Features);
            Assert.Equal(Company.KnownPlans.Count, snap.SelfServicePlans.Count);
            Assert.True(svc.Options.IsSelfServicePlan("enterprise"));
            Assert.True(svc.Evaluate(company.Id, new EntitlementAction.Create(MeteredResource.SalesDocuments)).Allowed);
            Assert.False(svc.Evaluate(company.Id, new EntitlementAction.Feature("mfa")).Allowed);
        }

        [Fact]
        public void KillSwitch_FeaturesOn_ResolveFeaturesSeamEmits_QuotasStillApply()
        {
            var company = FreeCompany();
            var harness = new ContractHarness
            {
                Options = new EntitlementsOptions { Enabled = true, FeaturesEnabled = true },
                Companies = new List<Company> { company },
                FreeLimits = { [MeteredResource.SalesDocuments] = 0 }
            };
            var withKeys = CreateFeatureSeamService(harness, ImmutableHashSet.Create("mfa"));
            Assert.True(withKeys.HasFeature(1, "mfa"));
            Assert.False(withKeys.Evaluate(1, new EntitlementAction.Create(MeteredResource.SalesDocuments)).Allowed);
        }

        [Fact]
        public void DenyShape_PlanNotAvailable_HasStableCode()
        {
            var company = FreeCompany();
            var options = new EntitlementsOptions
            {
                Enabled = true,
                FeaturesEnabled = true,
                SelfServicePlans = { "accounting_free" }
            };
            var svc = CreateService(options, company);
            var decision = DenyPlan(svc);
            Assert.Equal("PLAN_NOT_AVAILABLE", decision.Code);
            Assert.False(string.IsNullOrWhiteSpace(decision.Detail));
        }

        private static EntitlementDecision DenyPlan(IEntitlementService svc)
        {
            try
            {
                svc.EnsureSelfServicePlan("enterprise");
                return new EntitlementDecision(true, null, null);
            }
            catch (PlanNotAvailableException ex)
            {
                return new EntitlementDecision(false, ex.Code, ex.Message);
            }
        }

        private static IEntitlementService Build(ContractHarness harness, IReadOnlySet<string>? featuresWhenEnabled)
        {
            var companies = Repo(harness.Companies);
            var members = Repo(harness.Members);
            var invites = Repo(harness.Invitations);
            var usage = UsageRepo(harness.Usage);
            var banks = Repo(harness.Banks);
            var tenant = harness.Tenant ?? new Mock<ITenantContext>(MockBehavior.Strict);
            if (featuresWhenEnabled == null)
            {
                return new EntitlementService(
                    harness.Options, companies.Object, members.Object, invites.Object,
                    usage.Object, banks.Object, tenant.Object, harness.Time);
            }

            return new FeatureSeamEntitlementService(
                harness.Options, companies.Object, members.Object, invites.Object,
                usage.Object, banks.Object, tenant.Object, featuresWhenEnabled, harness.Time);
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

        private sealed class FeatureSeamEntitlementService : EntitlementService
        {
            private readonly IReadOnlySet<string> _features;

            public FeatureSeamEntitlementService(
                EntitlementsOptions options,
                IRepository<Company> companies,
                IRepository<CompanyMember> members,
                IRepository<CompanyInvitation> invitations,
                IRepository<EntitlementUsage> usage,
                IRepository<Bank> banks,
                ITenantContext tenant,
                IReadOnlySet<string> features,
                TimeProvider? time)
                : base(options, companies, members, invitations, usage, banks, tenant, time)
            {
                _features = features;
            }

            protected override IReadOnlySet<string> ResolveFeatures(PolicyMemo policy) => _features;
        }
    }
}
