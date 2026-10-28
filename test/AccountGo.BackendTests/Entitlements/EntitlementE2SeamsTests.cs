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
    public class EntitlementE2SeamsTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void EvaluateForUser_BlankUserId_ThrowsArgumentException(string? userId)
        {
            var svc = Build(new EntitlementsOptions { Enabled = true }, new List<Company> { FreeCompany() });
            var ex = Assert.Throws<ArgumentException>(() =>
                svc.EvaluateForUser(userId!, new UserAction.OwnAnotherWorkspace()));
            Assert.Equal("userId", ex.ParamName);
        }

        [Fact]
        public void OwnAnotherWorkspace_EnabledFalse_ShortCircuitsBeforeMembershipLookup()
        {
            var memberHits = 0;
            var members = CountingRepo<CompanyMember>(() => memberHits++);
            var svc = new EntitlementService(
                new EntitlementsOptions { Enabled = false },
                Repo(new List<Company> { FreeCompany() }).Object,
                members.Object,
                Repo(new List<CompanyInvitation>()).Object,
                Repo(new List<EntitlementUsage>()).Object,
                Repo(new List<Bank>()).Object,
                new Mock<ITenantContext>(MockBehavior.Strict).Object);

            var d = svc.EvaluateForUser("u1", new UserAction.OwnAnotherWorkspace());
            Assert.True(d.Allowed);
            Assert.Equal(0, memberHits);
        }

        [Fact]
        public void OwnAnotherWorkspace_Bypass_ShortCircuitsBeforeMembershipLookup()
        {
            var memberHits = 0;
            var members = CountingRepo<CompanyMember>(() => memberHits++);
            var svc = new EntitlementService(
                new EntitlementsOptions
                {
                    Enabled = true,
                    FreeLimits = { [MeteredResource.OwnedWorkspaces] = 0 }
                },
                Repo(new List<Company> { FreeCompany() }).Object,
                members.Object,
                Repo(new List<CompanyInvitation>()).Object,
                Repo(new List<EntitlementUsage>()).Object,
                Repo(new List<Bank>()).Object,
                new Mock<ITenantContext>(MockBehavior.Strict).Object);

            using (svc.Bypass())
            {
                var d = svc.EvaluateForUser("u1", new UserAction.OwnAnotherWorkspace());
                Assert.True(d.Allowed);
            }
            Assert.Equal(0, memberHits);
        }

        [Fact]
        public void ToException_QuotaDecision_ThrowsQuotaExceeded_NotModuleNotSubscribed()
        {
            var svc = Build(
                new EntitlementsOptions
                {
                    Enabled = true,
                    FreeLimits = { [MeteredResource.SalesDocuments] = 0 }
                },
                new List<Company> { FreeCompany() });

            var ex = Assert.Throws<QuotaExceededException>(() =>
                svc.Ensure(1, new EntitlementAction.Create(MeteredResource.SalesDocuments)));
            Assert.Equal("QUOTA_EXCEEDED", ex.Code);
            Assert.IsNotType<ModuleNotSubscribedException>(ex);
        }

        [Fact]
        public void EvaluateForUser_DoesNotTouchTenantContext()
        {
            var company = new Company
            {
                Id = 1,
                Name = "Co",
                Plan = "accounting_free",
                SubscriptionStatus = "active",
                DisabledModulesJson = "[]"
            };
            var members = new List<CompanyMember>
            {
                new() { Id = 1, CompanyId = 1, UserId = "u1", Role = CompanyMemberRole.Owner, Status = CompanyMemberStatus.Active }
            };
            var tenant = new Mock<ITenantContext>(MockBehavior.Strict);
            var svc = Build(
                new EntitlementsOptions
                {
                    Enabled = true,
                    FreeLimits = { [MeteredResource.OwnedWorkspaces] = 1 }
                },
                new List<Company> { company },
                members,
                tenant);

            var own = svc.EvaluateForUser("u1", new UserAction.OwnAnotherWorkspace());
            Assert.False(own.Allowed);
            Assert.Equal("QUOTA_EXCEEDED", own.Code);

            var mfa = svc.EvaluateForUser("u1", new UserAction.FeatureAmongMemberships("mfa"));
            Assert.False(mfa.Allowed);
            Assert.Equal("FEATURE_NOT_ENTITLED", mfa.Code);
            tenant.VerifyNoOtherCalls();
        }

        [Fact]
        public void Community_MfaUserActions_AlwaysDeny()
        {
            var company = new Company
            {
                Id = 1,
                Name = "Pro",
                Plan = "accounting_pro",
                SubscriptionStatus = "active",
                DisabledModulesJson = "[]"
            };
            var members = new List<CompanyMember>
            {
                new() { Id = 1, CompanyId = 1, UserId = "u1", Role = CompanyMemberRole.Member, Status = CompanyMemberStatus.Active }
            };
            var svc = Build(
                new EntitlementsOptions { Enabled = true, FeaturesEnabled = true },
                new List<Company> { company },
                members);

            Assert.False(svc.EvaluateForUser("u1", new UserAction.FeatureAmongMemberships("mfa")).Allowed);
            Assert.False(svc.EvaluateForUser("u1", new UserAction.FeatureAmongMemberships("mfa_policy")).Allowed);
            Assert.False(((IEntitlementService)svc).HasFeature(1, "mfa"));
        }

        [Fact]
        public void Ensure_Create_ReusesEnsureCanCreatePath()
        {
            var company = FreeCompany();
            var svc = Build(
                new EntitlementsOptions
                {
                    Enabled = true,
                    FreeLimits = { [MeteredResource.SalesDocuments] = 0 }
                },
                new List<Company> { company });

            var viaEnsure = Assert.Throws<QuotaExceededException>(() =>
                svc.Ensure(1, new EntitlementAction.Create(MeteredResource.SalesDocuments)));
            var viaLegacy = Assert.Throws<QuotaExceededException>(() =>
                svc.EnsureCanCreate(1, MeteredResource.SalesDocuments));

            Assert.Equal(viaLegacy.Message, viaEnsure.Message);
            Assert.Contains("Accounting Free", viaEnsure.Message);
            Assert.DoesNotContain("The Free plan", viaEnsure.Message);
            Assert.True(viaEnsure.UpgradeAvailable); // accounting_free → accounting_pro lifts quotas
        }

        [Fact]
        public void UpgradeAvailable_SubscriptionInactive_True_WhenEnabled()
        {
            var company = new Company
            {
                Id = 1,
                Name = "Ent",
                Plan = "enterprise",
                SubscriptionStatus = "canceled",
                DisabledModulesJson = "[]"
            };
            var svc = Build(new EntitlementsOptions { Enabled = true }, new List<Company> { company });
            var d = svc.Evaluate(1, new EntitlementAction.Module("inventory"));
            Assert.Equal("SUBSCRIPTION_INACTIVE", d.Code);
            Assert.True(d.UpgradeAvailable);
        }

        [Fact]
        public void UpgradeAvailable_False_WhenSelfHost()
        {
            var company = new Company
            {
                Id = 1,
                Name = "Ent",
                Plan = "enterprise",
                SubscriptionStatus = "canceled",
                DisabledModulesJson = "[]"
            };
            var svc = Build(new EntitlementsOptions { Enabled = false }, new List<Company> { company });
            // Enabled=false does not strip modules — inventory still allowed. Force MODULE deny via empty active.
            company.DisabledModulesJson = "[\"inventory\",\"donations\",\"audit\",\"financials\",\"purchases\",\"reports\"]";
            svc.ClearPolicyMemo(1);
            var moduleDeny = svc.Evaluate(1, new EntitlementAction.Module("inventory"));
            Assert.Equal("MODULE_NOT_SUBSCRIBED", moduleDeny.Code);
            Assert.False(moduleDeny.UpgradeAvailable);

            var featureDeny = svc.Evaluate(1, new EntitlementAction.Feature("mfa"));
            Assert.False(featureDeny.UpgradeAvailable);
        }

        [Fact]
        public void UpgradeAvailable_Module_True_WhenTransitionAddsModule()
        {
            // invoicing_free has no financials; transition to accounting_free would add it.
            var company = new Company
            {
                Id = 1,
                Name = "Inv",
                Plan = "invoicing_free",
                SubscriptionStatus = "active",
                DisabledModulesJson = "[]"
            };
            var svc = Build(new EntitlementsOptions { Enabled = true }, new List<Company> { company });
            var d = svc.Evaluate(1, new EntitlementAction.Module("financials"));
            Assert.Equal("MODULE_NOT_SUBSCRIBED", d.Code);
            Assert.True(d.UpgradeAvailable);
        }

        [Fact]
        public void Record_WithCompanyId_DoesNotRequireTenant()
        {
            var company = FreeCompany();
            var usage = new List<EntitlementUsage>();
            var usageRepo = new Mock<IRepository<EntitlementUsage>>();
            usageRepo.Setup(r => r.Table).Returns(() => usage.AsQueryable());
            usageRepo.Setup(r => r.Insert(It.IsAny<EntitlementUsage>())).Callback<EntitlementUsage>(u =>
            {
                u.Id = usage.Count + 1;
                usage.Add(u);
            });
            var tenant = new Mock<ITenantContext>(MockBehavior.Strict);
            var svc = new EntitlementService(
                new EntitlementsOptions { Enabled = true },
                Repo(new List<Company> { company }).Object,
                Repo(new List<CompanyMember>()).Object,
                Repo(new List<CompanyInvitation>()).Object,
                usageRepo.Object,
                Repo(new List<Bank>()).Object,
                tenant.Object);

            svc.Record(company.Id, MeteredResource.SalesDocuments, 42);
            Assert.Single(usage);
            Assert.Equal(company.Id, usage[0].CompanyId);
            tenant.VerifyNoOtherCalls();
        }

        [Fact]
        public void EnsureCanOwnAnotherWorkspace_DelegatesToEvaluateForUser()
        {
            var company = FreeCompany();
            var members = new List<CompanyMember>
            {
                new() { Id = 1, CompanyId = 1, UserId = "u1", Role = CompanyMemberRole.Owner, Status = CompanyMemberStatus.Active }
            };
            var svc = Build(
                new EntitlementsOptions
                {
                    Enabled = true,
                    FreeLimits = { [MeteredResource.OwnedWorkspaces] = 1 }
                },
                new List<Company> { company },
                members);

            var ex = Assert.Throws<QuotaExceededException>(() => svc.EnsureCanOwnAnotherWorkspace("u1"));
            Assert.Equal(MeteredResource.OwnedWorkspaces, ex.Resource);
        }

        private static Company FreeCompany() => new()
        {
            Id = 1,
            Name = "Free",
            Plan = "accounting_free",
            SubscriptionStatus = "active",
            DisabledModulesJson = "[]"
        };

        private static EntitlementService Build(
            EntitlementsOptions options,
            List<Company> companies,
            List<CompanyMember>? members = null,
            Mock<ITenantContext>? tenant = null) =>
            new(
                options,
                Repo(companies).Object,
                Repo(members ?? new List<CompanyMember>()).Object,
                Repo(new List<CompanyInvitation>()).Object,
                Repo(new List<EntitlementUsage>()).Object,
                Repo(new List<Bank>()).Object,
                (tenant ?? new Mock<ITenantContext>()).Object);

        private static Mock<IRepository<T>> Repo<T>(List<T> data) where T : BaseEntity
        {
            var mock = new Mock<IRepository<T>>();
            mock.Setup(r => r.Table).Returns(() => data.AsQueryable());
            mock.Setup(r => r.GetById(It.IsAny<object>())).Returns((object id) =>
                data.FirstOrDefault(e => e.Id.Equals(Convert.ToInt32(id)))!);
            return mock;
        }

        private static Mock<IRepository<T>> CountingRepo<T>(Action onTable) where T : BaseEntity
        {
            var mock = new Mock<IRepository<T>>();
            mock.Setup(r => r.Table).Returns(() =>
            {
                onTable();
                return new List<T>().AsQueryable();
            });
            return mock;
        }
    }
}
