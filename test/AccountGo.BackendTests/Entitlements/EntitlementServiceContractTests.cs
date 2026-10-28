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
    /// <summary>
    /// Shared contract any <see cref="IEntitlementService"/> must pass.
    /// Commercial repos subclass and override <see cref="CreateService"/> / <see cref="CreateFeatureSeamService"/>.
    /// </summary>
    public abstract class EntitlementServiceContractTests
    {
        protected abstract IEntitlementService CreateService(EntitlementsOptions options, Company company);

        /// <summary>Optional seam that can emit feature keys when FeaturesEnabled is true (test-only).</summary>
        protected abstract IEntitlementService CreateFeatureSeamService(
            EntitlementsOptions options, Company company, IReadOnlySet<string> featuresWhenEnabled);

        [Fact]
        public void SelfHost_EnabledFalse_Unlimited_AllPlansSelfService_NoFeatures()
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
        public void KillSwitch_QuotasOff_DoesNotClearModulesOrLifecycle()
        {
            var company = new Company
            {
                Id = 1,
                Name = "Co",
                Plan = "enterprise",
                SubscriptionStatus = "active",
                DisabledModulesJson = "[]"
            };
            var svc = CreateService(new EntitlementsOptions { Enabled = false, FeaturesEnabled = true }, company);
            var snap = svc.Get(1);
            Assert.False(snap.Limited);
            Assert.Contains("inventory", snap.Modules, StringComparer.OrdinalIgnoreCase);
            Assert.True(svc.Evaluate(1, new EntitlementAction.Module("inventory")).Allowed);
        }

        [Fact]
        public void KillSwitch_FeaturesOff_DoesNotAffectQuotas()
        {
            var company = FreeCompany();
            var options = new EntitlementsOptions
            {
                Enabled = true,
                FeaturesEnabled = false,
                FreeLimits = { [MeteredResource.SalesDocuments] = 0 }
            };
            var withKeys = CreateFeatureSeamService(options, company, ImmutableHashSet.Create("mfa"));
            Assert.Empty(withKeys.Get(1).Features);
            Assert.False(withKeys.HasFeature(1, "mfa"));

            var deny = withKeys.Evaluate(1, new EntitlementAction.Create(MeteredResource.SalesDocuments));
            Assert.False(deny.Allowed);
            Assert.Equal("QUOTA_EXCEEDED", deny.Code);
        }

        [Fact]
        public void KillSwitch_FeaturesOn_SeamEmits_QuotasStillApply()
        {
            var company = FreeCompany();
            var options = new EntitlementsOptions
            {
                Enabled = true,
                FeaturesEnabled = true,
                FreeLimits = { [MeteredResource.SalesDocuments] = 0 }
            };
            var withKeys = CreateFeatureSeamService(options, company, ImmutableHashSet.Create("mfa"));
            Assert.True(withKeys.HasFeature(1, "mfa"));
            Assert.False(withKeys.Evaluate(1, new EntitlementAction.Create(MeteredResource.SalesDocuments)).Allowed);
        }

        [Theory]
        [InlineData("QUOTA_EXCEEDED")]
        [InlineData("MODULE_NOT_SUBSCRIBED")]
        [InlineData("FEATURE_NOT_ENTITLED")]
        [InlineData("SUBSCRIPTION_INACTIVE")]
        [InlineData("PLAN_NOT_AVAILABLE")]
        public void DenyShape_EveryCode_HasStableFields(string code)
        {
            var company = FreeCompany();
            var options = new EntitlementsOptions
            {
                Enabled = true,
                FeaturesEnabled = true,
                FreeLimits = { [MeteredResource.SalesDocuments] = 0 },
                SelfServicePlans = { "accounting_free" }
            };
            var svc = CreateService(options, company);
            EntitlementDecision decision = code switch
            {
                "QUOTA_EXCEEDED" => svc.Evaluate(1, new EntitlementAction.Create(MeteredResource.SalesDocuments)),
                "MODULE_NOT_SUBSCRIBED" => svc.Evaluate(1, new EntitlementAction.Module("inventory")),
                "FEATURE_NOT_ENTITLED" => svc.Evaluate(1, new EntitlementAction.Feature("mfa")),
                "SUBSCRIPTION_INACTIVE" => CreateService(options, LapsedEnterprise()).Evaluate(1, new EntitlementAction.Module("inventory")),
                "PLAN_NOT_AVAILABLE" => DenyPlan(svc),
                _ => throw new InvalidOperationException(code)
            };

            if (code == "PLAN_NOT_AVAILABLE")
            {
                Assert.Equal("PLAN_NOT_AVAILABLE", decision.Code);
                return;
            }

            Assert.False(decision.Allowed);
            Assert.Equal(code, decision.Code);
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

        protected static Company FreeCompany() => new()
        {
            Id = 1,
            Name = "Free",
            Plan = "accounting_free",
            SubscriptionStatus = "active",
            DisabledModulesJson = "[]"
        };

        protected static Company LapsedEnterprise() => new()
        {
            Id = 1,
            Name = "Ent",
            Plan = "enterprise",
            SubscriptionStatus = "canceled",
            DisabledModulesJson = "[]"
        };
    }

    public sealed class CommunityEntitlementServiceContractTests : EntitlementServiceContractTests
    {
        protected override IEntitlementService CreateService(EntitlementsOptions options, Company company) =>
            Build(options, company, featuresWhenEnabled: null);

        protected override IEntitlementService CreateFeatureSeamService(
            EntitlementsOptions options, Company company, IReadOnlySet<string> featuresWhenEnabled) =>
            Build(options, company, featuresWhenEnabled);

        private static EntitlementService Build(
            EntitlementsOptions options, Company company, IReadOnlySet<string>? featuresWhenEnabled)
        {
            var companies = Repo(new List<Company> { company });
            var members = Repo(new List<CompanyMember>());
            var invites = Repo(new List<CompanyInvitation>());
            var usage = Repo(new List<EntitlementUsage>());
            var banks = Repo(new List<Bank>());
            var tenant = new Mock<ITenantContext>(MockBehavior.Strict);
            if (featuresWhenEnabled == null)
            {
                return new EntitlementService(
                    options, companies.Object, members.Object, invites.Object,
                    usage.Object, banks.Object, tenant.Object);
            }

            return new FeatureSeamEntitlementService(
                options, companies.Object, members.Object, invites.Object,
                usage.Object, banks.Object, tenant.Object, featuresWhenEnabled);
        }

        private static Mock<IRepository<T>> Repo<T>(List<T> data) where T : BaseEntity
        {
            var mock = new Mock<IRepository<T>>();
            mock.Setup(r => r.Table).Returns(() => data.AsQueryable());
            mock.Setup(r => r.GetById(It.IsAny<object>())).Returns((object id) =>
                data.FirstOrDefault(e => e.Id.Equals(Convert.ToInt32(id)))!);
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
                IReadOnlySet<string> features)
                : base(options, companies, members, invitations, usage, banks, tenant)
            {
                _features = features;
            }

            protected override IReadOnlySet<string> ResolveFeatures(PolicyMemo policy) => _features;
        }
    }
}
