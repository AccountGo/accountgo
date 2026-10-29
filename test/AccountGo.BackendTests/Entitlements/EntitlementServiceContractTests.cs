// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Core.Domain;
using Core.Domain.Financials;
using Core.Entitlements;
using Core.Tenancy;
using Moq;
using Xunit;

namespace AccountGo.BackendTests.Entitlements
{
    /// <summary>
    /// Engine-agnostic invariants any <see cref="IEntitlementService"/> must pass.
    /// Other implementations may link this file and subclass it.
    /// </summary>
    public abstract class EntitlementServiceContractTests
    {
        /// <summary>Build the engine under test. Tenant defaults to Strict when null.</summary>
        protected abstract IEntitlementService CreateService(ContractHarness harness);

        /// <summary>
        /// Optional seam that can emit feature keys when FeaturesEnabled is true (test-only).
        /// Default: same as <see cref="CreateService"/>.
        /// </summary>
        protected virtual IEntitlementService CreateFeatureSeamService(
            ContractHarness harness, IReadOnlySet<string> featuresWhenEnabled) =>
            CreateService(harness);

        /// <summary>
        /// Apply <see cref="ContractHarness.FreeLimits"/> for engines that support test caps.
        /// Default: no-op (community has no quotas).
        /// </summary>
        protected virtual void ApplyHarnessFreeLimits(ContractHarness harness) { }

        protected sealed class ContractHarness
        {
            public EntitlementsOptions Options { get; init; } = new();
            /// <summary>Free-tier meter caps for engines that honour them via <see cref="ApplyHarnessFreeLimits"/>.</summary>
            public Dictionary<MeteredResource, int> FreeLimits { get; init; } = new();
            public List<Company> Companies { get; init; } = new();
            public List<CompanyMember> Members { get; init; } = new();
            public List<CompanyInvitation> Invitations { get; init; } = new();
            public List<EntitlementUsage> Usage { get; init; } = new();
            public List<Bank> Banks { get; init; } = new();
            public TimeProvider? Time { get; init; }
            public Mock<ITenantContext>? Tenant { get; init; }
        }

        protected IEntitlementService CreateService(EntitlementsOptions options, Company company, TimeProvider? time = null) =>
            CreateService(new ContractHarness
            {
                Options = options,
                Companies = new List<Company> { company },
                Time = time
            });

        [Fact]
        public void KillSwitch_EnabledFalse_DoesNotClearModules()
        {
            var company = new Company
            {
                Id = 1,
                Name = "Co",
                Plan = "community",
                SubscriptionStatus = "active",
                DisabledModulesJson = "[]"
            };
            var svc = CreateService(new EntitlementsOptions { Enabled = false, FeaturesEnabled = true }, company);
            var snap = svc.Get(1);
            Assert.Contains("sales", snap.Modules, StringComparer.OrdinalIgnoreCase);
            Assert.True(svc.Evaluate(1, new EntitlementAction.Module("sales")).Allowed);
        }

        [Fact]
        public void KillSwitch_FeaturesOff_EmptiesOrKeepsEngineFeaturesIndependentOfQuotas()
        {
            var company = FreeCompany();
            var harness = new ContractHarness
            {
                Options = new EntitlementsOptions { Enabled = true, FeaturesEnabled = false },
                Companies = new List<Company> { company },
            };
            var withKeys = CreateFeatureSeamService(harness, ImmutableHashSet.Create("mfa"));
            Assert.Empty(withKeys.Get(1).Features);
            Assert.False(withKeys.HasFeature(1, "mfa"));
        }

        [Fact]
        public void Deny_FeatureNotEntitled_SetsRequiredFeature()
        {
            var svc = CreateService(new EntitlementsOptions { Enabled = true, FeaturesEnabled = true }, FreeCompany());
            var d = svc.Evaluate(1, new EntitlementAction.Feature("mfa"));
            Assert.False(d.Allowed);
            Assert.Equal("FEATURE_NOT_ENTITLED", d.Code);
            Assert.Equal("mfa", d.RequiredFeature);
            Assert.False(string.IsNullOrWhiteSpace(d.Detail));
        }

        [Fact]
        public void Evaluate_NeverThrows_ForModuleFeatureCreate()
        {
            var svc = CreateService(new EntitlementsOptions { Enabled = true, FeaturesEnabled = true }, FreeCompany());
            Assert.Null(Record.Exception(() => svc.Evaluate(1, new EntitlementAction.Module("sales"))));
            Assert.Null(Record.Exception(() => svc.Evaluate(1, new EntitlementAction.Feature("mfa"))));
            Assert.Null(Record.Exception(() => svc.Evaluate(1, new EntitlementAction.Create(MeteredResource.SalesDocuments))));
        }

        [Fact]
        public void Ensure_ThrowsTypedException_OnFeatureDeny()
        {
            var svc = CreateService(new EntitlementsOptions { Enabled = true, FeaturesEnabled = true }, FreeCompany());
            var feature = Assert.ThrowsAny<EntitlementException>(() =>
                svc.Ensure(1, new EntitlementAction.Feature("mfa")));
            Assert.Equal("FEATURE_NOT_ENTITLED", feature.Code);
            Assert.IsType<FeatureNotEntitledException>(feature);
            Assert.Equal("mfa", feature.RequiredFeature);
        }

        [Fact]
        public void EvaluateForUser_NeverTouchesTenantContext()
        {
            var company = FreeCompany();
            var members = new List<CompanyMember>
            {
                new() { Id = 1, CompanyId = 1, UserId = "u1", Role = CompanyMemberRole.Owner, Status = CompanyMemberStatus.Active }
            };
            var tenant = new Mock<ITenantContext>(MockBehavior.Strict);
            var svc = CreateService(new ContractHarness
            {
                Options = new EntitlementsOptions { Enabled = true },
                Companies = new List<Company> { company },
                Members = members,
                Tenant = tenant,
            });

            var mfa = svc.EvaluateForUser("u1", new UserAction.FeatureAmongMemberships("mfa"));
            Assert.False(mfa.Allowed);
            Assert.Equal("FEATURE_NOT_ENTITLED", mfa.Code);
            tenant.VerifyNoOtherCalls();
        }

        [Fact]
        public void Record_WithCompanyId_WritesThatCompanyId()
        {
            var company = FreeCompany();
            var usage = new List<EntitlementUsage>();
            var tenant = new Mock<ITenantContext>(MockBehavior.Strict);
            var svc = CreateService(new ContractHarness
            {
                Options = new EntitlementsOptions { Enabled = true },
                Companies = new List<Company> { company },
                Usage = usage,
                Tenant = tenant
            });

            svc.Record(company.Id, MeteredResource.SalesDocuments, 42);
            Assert.Single(usage);
            Assert.Equal(company.Id, usage[0].CompanyId);
            tenant.VerifyNoOtherCalls();
        }

        [Fact]
        public void Version_StableAcrossCalls_AndExcludesUsage()
        {
            var company = FreeCompany();
            var usage = new List<EntitlementUsage>();
            var svc = CreateService(new ContractHarness
            {
                Options = new EntitlementsOptions { Enabled = true },
                Companies = new List<Company> { company },
                Usage = usage
            });

            var first = svc.Get(1).Version;
            Assert.False(string.IsNullOrWhiteSpace(first));
            Assert.Equal(first, svc.Get(1).Version);

            svc.Record(1, MeteredResource.SalesDocuments, 7);
            Assert.Single(usage);
            Assert.Equal(first, svc.Get(1).Version);
        }

        protected static Company FreeCompany() => new()
        {
            Id = 1,
            Name = "Free",
            Plan = "community",
            SubscriptionStatus = "active",
            DisabledModulesJson = "[]"
        };

        protected sealed class FixedTimeProvider : TimeProvider
        {
            private readonly DateTimeOffset _utc;
            public FixedTimeProvider(DateTimeOffset utc) => _utc = utc;
            public override DateTimeOffset GetUtcNow() => _utc;
        }
    }
}
