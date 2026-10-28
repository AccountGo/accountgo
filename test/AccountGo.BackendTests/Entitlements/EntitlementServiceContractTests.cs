// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
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
    /// Shared contract any <see cref="IEntitlementService"/> must pass.
    /// Other implementations may link this file and subclass it.
    /// Expectations are hand-written literals — not derived from engine helpers.
    /// </summary>
    public abstract class EntitlementServiceContractTests
    {
        /// <summary>Fixed clock for §6.2 grace boundary cases.</summary>
        protected static readonly DateTime GraceAnchor =
            DateTime.Parse("2026-06-15T12:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        protected static readonly DateTime DeterminationNow =
            DateTime.Parse("2026-06-20T12:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        /// <summary>Build the engine under test. Tenant defaults to Strict when null.</summary>
        protected abstract IEntitlementService CreateService(ContractHarness harness);

        /// <summary>
        /// Optional seam that can emit feature keys when FeaturesEnabled is true (test-only).
        /// Used by the FeaturesEnabled kill-switch cases that need features present when the switch is on.
        /// </summary>
        protected abstract IEntitlementService CreateFeatureSeamService(
            ContractHarness harness, IReadOnlySet<string> featuresWhenEnabled);

        /// <summary>
        /// Apply <see cref="ContractHarness.FreeLimits"/> without using community-only
        /// <see cref="EntitlementsOptions.FreeLimits"/> as the shared contract surface.
        /// Community copies into options; hosted uses a test-only catalog override.
        /// </summary>
        protected abstract void ApplyHarnessFreeLimits(ContractHarness harness);

        protected sealed class ContractHarness
        {
            public EntitlementsOptions Options { get; init; } = new();
            /// <summary>
            /// Free-tier meter caps for quota deny cases. Engines apply via
            /// <see cref="ApplyHarnessFreeLimits"/> — not by reading Options.FreeLimits in the shared tests.
            /// </summary>
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

        // ── §8.1 kill switches (shared) ──────────────────────────────────────────

        [Fact]
        public void KillSwitch_QuotasOff_DoesNotClearModulesOrLifecycleOrFeatures()
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
            // Lifecycle still resolved (active); features remain whatever the engine emits when FeaturesEnabled.
            Assert.NotNull(snap.Lifecycle);
            Assert.Equal("active", snap.Lifecycle!.Status);
        }

        [Fact]
        public void KillSwitch_FeaturesOff_EmptiesFeatures_WithoutChangingQuotas()
        {
            var company = FreeCompany();
            var harness = new ContractHarness
            {
                Options = new EntitlementsOptions { Enabled = true, FeaturesEnabled = false },
                Companies = new List<Company> { company },
                FreeLimits = { [MeteredResource.SalesDocuments] = 0 }
            };
            var withKeys = CreateFeatureSeamService(harness, ImmutableHashSet.Create("mfa"));
            Assert.Empty(withKeys.Get(1).Features);
            Assert.False(withKeys.HasFeature(1, "mfa"));

            var deny = withKeys.Evaluate(1, new EntitlementAction.Create(MeteredResource.SalesDocuments));
            Assert.False(deny.Allowed);
            Assert.Equal("QUOTA_EXCEEDED", deny.Code);
            Assert.Equal(MeteredResource.SalesDocuments, deny.Resource);
            Assert.Equal(0, deny.Limit);
            Assert.Equal(0, deny.Used);
        }

        // ── §6.2 lifecycle rows a–h ───────────────────────────────────────────────

        public static IEnumerable<object[]> LifecycleTable()
        {
            // plan, status, tier, billingManual, periodLabel, module, access, expectAllow, expectCode, expectReadOnlyContains
            // a) comp / BillingManagedManually first
            yield return Row("enterprise", "canceled", "comp", false, "null", "inventory", ModuleAccess.Write, true, null, false);
            yield return Row("accounting_pro", "canceled", null, true, "null", "financials", ModuleAccess.Write, true, null, false);
            // b) free stored plan not reduced
            yield return Row("accounting_free", "canceled", null, false, "null", "reports", ModuleAccess.Write, true, null, false);
            // c) active / trialing / blank
            yield return Row("enterprise", "active", null, false, "null", "inventory", ModuleAccess.Write, true, null, false);
            yield return Row("enterprise", "trialing", null, false, "null", "inventory", ModuleAccess.Write, true, null, false);
            yield return Row("enterprise", "", null, false, "null", "inventory", ModuleAccess.Write, true, null, false);
            yield return Row("enterprise", "   ", null, false, "null", "audit", ModuleAccess.Write, true, null, false);
            // d) past_due within grace
            yield return Row("enterprise", "past_due", null, false, "within", "inventory", ModuleAccess.Write, true, null, false);
            // e) past_due beyond grace → read-only for paid-only modules
            yield return Row("enterprise", "past_due", null, false, "beyond", "inventory", ModuleAccess.Write, false, "SUBSCRIPTION_INACTIVE", true);
            yield return Row("enterprise", "past_due", null, false, "beyond", "inventory", ModuleAccess.Read, true, null, true);
            yield return Row("enterprise", "past_due", null, false, "beyond", "sales", ModuleAccess.Write, true, null, false);
            // f/g) canceled / unpaid / incomplete_expired / incomplete
            yield return Row("enterprise", "canceled", null, false, "null", "inventory", ModuleAccess.Write, false, "SUBSCRIPTION_INACTIVE", true);
            yield return Row("enterprise", "unpaid", null, false, "null", "donations", ModuleAccess.Write, false, "SUBSCRIPTION_INACTIVE", true);
            yield return Row("enterprise", "incomplete_expired", null, false, "null", "inventory", ModuleAccess.Write, false, "SUBSCRIPTION_INACTIVE", true);
            yield return Row("enterprise", "incomplete", null, false, "null", "inventory", ModuleAccess.Write, false, "SUBSCRIPTION_INACTIVE", true);
            // h) unknown non-empty → free
            yield return Row("enterprise", "weird_status", null, false, "null", "inventory", ModuleAccess.Write, false, "SUBSCRIPTION_INACTIVE", true);
            // opt-out while lapsed: MODULE_NOT_SUBSCRIBED (stored plan no longer has the module via opt-out)
            yield return Row("accounting_pro", "canceled", null, false, "null", "reports", ModuleAccess.Write, false, "MODULE_NOT_SUBSCRIBED", false, disabled: "[\"reports\"]");
        }

        private static object[] Row(
            string plan, string status, string? tier, bool billingManual, string periodLabel,
            string module, ModuleAccess access, bool allow, string? code, bool readOnlyContains,
            string? disabled = null) =>
            new object[] { plan, status, tier ?? "", billingManual, periodLabel, module, access, allow, code ?? "", readOnlyContains, disabled ?? "" };

        [Theory]
        [MemberData(nameof(LifecycleTable))]
        public void Lifecycle_Section62_RowsMatchEvaluate(
            string plan, string status, string tier, bool billingManual, string periodLabel,
            string module, ModuleAccess access, bool expectAllow, string expectCode, bool expectReadOnlyContains,
            string disabledJson)
        {
            DateTime? periodEnd = periodLabel switch
            {
                "within" => DeterminationNow.AddDays(-3),
                "beyond" => DeterminationNow.AddDays(-20),
                _ => null
            };

            var company = new Company
            {
                Id = 1,
                Name = "T",
                Plan = plan,
                SubscriptionStatus = status,
                Tier = string.IsNullOrEmpty(tier) ? null : tier,
                BillingManagedManually = billingManual,
                CurrentPeriodEnd = periodEnd,
                DisabledModulesJson = string.IsNullOrEmpty(disabledJson) ? "[]" : disabledJson
            };

            var svc = CreateService(
                new EntitlementsOptions { Enabled = true },
                company,
                new FixedTimeProvider(new DateTimeOffset(DeterminationNow, TimeSpan.Zero)));
            var decision = svc.Evaluate(1, new EntitlementAction.Module(access, module));
            var snapshot = svc.Get(1);

            Assert.Equal(expectAllow, decision.Allowed);
            Assert.Equal(string.IsNullOrEmpty(expectCode) ? null : expectCode, decision.Code);
            Assert.Equal(expectReadOnlyContains, snapshot.ReadOnlyModules.Contains(module, StringComparer.OrdinalIgnoreCase));
        }

        [Fact]
        public void PastDue_GraceBoundary_Inclusive_KeepsPaidModules()
        {
            var periodEnd = GraceAnchor;
            var company = Enterprise("past_due", periodEnd);
            var atGraceEnd = periodEnd.AddDays(14);
            var svc = CreateService(new EntitlementsOptions { Enabled = true }, company,
                new FixedTimeProvider(new DateTimeOffset(atGraceEnd, TimeSpan.Zero)));
            Assert.True(svc.Evaluate(1, new EntitlementAction.Module("inventory")).Allowed);
        }

        [Fact]
        public void PastDue_GraceBoundary_PlusOneSecond_IsReduced()
        {
            var periodEnd = GraceAnchor;
            var company = Enterprise("past_due", periodEnd);
            var justAfter = periodEnd.AddDays(14).AddSeconds(1);
            var svc = CreateService(new EntitlementsOptions { Enabled = true }, company,
                new FixedTimeProvider(new DateTimeOffset(justAfter, TimeSpan.Zero)));
            var decision = svc.Evaluate(1, new EntitlementAction.Module("inventory"));
            Assert.False(decision.Allowed);
            Assert.Equal("SUBSCRIPTION_INACTIVE", decision.Code);
        }

        [Fact]
        public void PastDue_NullPeriodEnd_IsReduced()
        {
            var company = Enterprise("past_due", periodEnd: null);
            var svc = CreateService(new EntitlementsOptions { Enabled = true }, company,
                new FixedTimeProvider(new DateTimeOffset(GraceAnchor, TimeSpan.Zero)));
            Assert.Equal("SUBSCRIPTION_INACTIVE",
                svc.Evaluate(1, new EntitlementAction.Module("inventory")).Code);
        }

        [Fact]
        public void PastDue_UnspecifiedPeriodEnd_TreatedAsUtc()
        {
            var periodEnd = DateTime.SpecifyKind(GraceAnchor.AddDays(-3), DateTimeKind.Unspecified);
            var company = Enterprise("past_due", periodEnd);
            var svc = CreateService(new EntitlementsOptions { Enabled = true }, company,
                new FixedTimeProvider(new DateTimeOffset(GraceAnchor, TimeSpan.Zero)));
            Assert.True(svc.Evaluate(1, new EntitlementAction.Module("inventory")).Allowed);
        }

        // ── Deny codes ───────────────────────────────────────────────────────────

        [Fact]
        public void Deny_SubscriptionInactive_OnlyWhenStoredPlanHasModule()
        {
            var lapsed = Enterprise("canceled");
            var svc = CreateService(new EntitlementsOptions { Enabled = true }, lapsed);
            var inventory = svc.Evaluate(1, new EntitlementAction.Module(ModuleAccess.Write, "inventory"));
            Assert.False(inventory.Allowed);
            Assert.Equal("SUBSCRIPTION_INACTIVE", inventory.Code);

            // Opted-out reports on accounting_pro → MODULE_NOT_SUBSCRIBED even when canceled
            var opted = new Company
            {
                Id = 1,
                Name = "Pro",
                Plan = "accounting_pro",
                SubscriptionStatus = "canceled",
                DisabledModulesJson = "[\"reports\"]"
            };
            var svc2 = CreateService(new EntitlementsOptions { Enabled = true }, opted);
            var reports = svc2.Evaluate(1, new EntitlementAction.Module(ModuleAccess.Write, "reports"));
            Assert.False(reports.Allowed);
            Assert.Equal("MODULE_NOT_SUBSCRIBED", reports.Code);
        }

        [Fact]
        public void Deny_FeatureNotEntitled_SetsRequiredFeature()
        {
            var svc = CreateService(new EntitlementsOptions { Enabled = true, FeaturesEnabled = true }, FreeCompany());
            var d = svc.Evaluate(1, new EntitlementAction.Feature("mfa"));
            Assert.False(d.Allowed);
            Assert.Equal("FEATURE_NOT_ENTITLED", d.Code);
            Assert.Equal("mfa", d.RequiredFeature);
        }

        [Fact]
        public void Deny_QuotaExceeded_SetsResourceLimitUsed()
        {
            var svc = CreateService(new ContractHarness
            {
                Options = new EntitlementsOptions { Enabled = true },
                Companies = new List<Company> { FreeCompany() },
                FreeLimits = { [MeteredResource.SalesDocuments] = 0 }
            });
            var d = svc.Evaluate(1, new EntitlementAction.Create(MeteredResource.SalesDocuments));
            Assert.False(d.Allowed);
            Assert.Equal("QUOTA_EXCEEDED", d.Code);
            Assert.Equal(MeteredResource.SalesDocuments, d.Resource);
            Assert.Equal(0, d.Limit);
            Assert.Equal(0, d.Used);
        }

        [Fact]
        public void Deny_ModuleNotSubscribed_ForModuleOutsidePlan()
        {
            var svc = CreateService(new EntitlementsOptions { Enabled = true }, FreeCompany());
            var d = svc.Evaluate(1, new EntitlementAction.Module("inventory"));
            Assert.False(d.Allowed);
            Assert.Equal("MODULE_NOT_SUBSCRIBED", d.Code);
        }

        [Theory]
        [InlineData("QUOTA_EXCEEDED")]
        [InlineData("MODULE_NOT_SUBSCRIBED")]
        [InlineData("FEATURE_NOT_ENTITLED")]
        [InlineData("SUBSCRIPTION_INACTIVE")]
        public void DenyShape_EveryCode_HasStableFields(string code)
        {
            var harness = new ContractHarness
            {
                Options = new EntitlementsOptions { Enabled = true, FeaturesEnabled = true },
                Companies = new List<Company> { FreeCompany() },
                FreeLimits = { [MeteredResource.SalesDocuments] = 0 }
            };
            var svc = CreateService(harness);
            EntitlementDecision decision = code switch
            {
                "QUOTA_EXCEEDED" => svc.Evaluate(1, new EntitlementAction.Create(MeteredResource.SalesDocuments)),
                "MODULE_NOT_SUBSCRIBED" => svc.Evaluate(1, new EntitlementAction.Module("inventory")),
                "FEATURE_NOT_ENTITLED" => svc.Evaluate(1, new EntitlementAction.Feature("mfa")),
                "SUBSCRIPTION_INACTIVE" => CreateService(new EntitlementsOptions { Enabled = true }, Enterprise("canceled"))
                    .Evaluate(1, new EntitlementAction.Module("inventory")),
                _ => throw new InvalidOperationException(code)
            };

            Assert.False(decision.Allowed);
            Assert.Equal(code, decision.Code);
            Assert.False(string.IsNullOrWhiteSpace(decision.Detail));
        }

        // ── Evaluate / Ensure / EvaluateForUser / Record ─────────────────────────

        [Fact]
        public void Evaluate_NeverThrows_ForModuleFeatureCreate()
        {
            var svc = CreateService(new EntitlementsOptions { Enabled = true, FeaturesEnabled = true }, FreeCompany());
            var module = Record.Exception(() => svc.Evaluate(1, new EntitlementAction.Module("inventory")));
            var feature = Record.Exception(() => svc.Evaluate(1, new EntitlementAction.Feature("mfa")));
            var create = Record.Exception(() => svc.Evaluate(1, new EntitlementAction.Create(MeteredResource.SalesDocuments)));
            Assert.Null(module);
            Assert.Null(feature);
            Assert.Null(create);
        }

        [Fact]
        public void Ensure_ThrowsTypedException_OnDeny()
        {
            var svc = CreateService(new ContractHarness
            {
                Options = new EntitlementsOptions { Enabled = true, FeaturesEnabled = true },
                Companies = new List<Company> { FreeCompany() },
                FreeLimits = { [MeteredResource.SalesDocuments] = 0 }
            });

            var quota = Assert.ThrowsAny<EntitlementException>(() =>
                svc.Ensure(1, new EntitlementAction.Create(MeteredResource.SalesDocuments)));
            Assert.Equal("QUOTA_EXCEEDED", quota.Code);
            Assert.IsType<QuotaExceededException>(quota);

            var module = Assert.ThrowsAny<EntitlementException>(() =>
                svc.Ensure(1, new EntitlementAction.Module("inventory")));
            Assert.Equal("MODULE_NOT_SUBSCRIBED", module.Code);

            var feature = Assert.ThrowsAny<EntitlementException>(() =>
                svc.Ensure(1, new EntitlementAction.Feature("mfa")));
            Assert.Equal("FEATURE_NOT_ENTITLED", feature.Code);
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
                FreeLimits = { [MeteredResource.OwnedWorkspaces] = 1 }
            });

            var own = svc.EvaluateForUser("u1", new UserAction.OwnAnotherWorkspace());
            Assert.False(own.Allowed);
            Assert.Equal("QUOTA_EXCEEDED", own.Code);

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

        // ── Version ──────────────────────────────────────────────────────────────

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

        // ── Helpers ──────────────────────────────────────────────────────────────

        protected static Company FreeCompany() => new()
        {
            Id = 1,
            Name = "Free",
            Plan = "accounting_free",
            SubscriptionStatus = "active",
            DisabledModulesJson = "[]"
        };

        protected static Company Enterprise(string status, DateTime? periodEnd = null) => new()
        {
            Id = 1,
            Name = "Ent",
            Plan = "enterprise",
            SubscriptionStatus = status,
            CurrentPeriodEnd = periodEnd,
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
