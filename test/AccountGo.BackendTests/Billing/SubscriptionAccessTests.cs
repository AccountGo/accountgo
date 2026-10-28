// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Core.Billing;
using Core.Domain;
using Xunit;

namespace AccountGo.BackendTests.Billing;

public class SubscriptionAccessTests
{
    [Fact]
    public void Active_IsNotInactive()
    {
        var company = new Company { SubscriptionStatus = "active", Plan = "accounting_pro" };
        Assert.False(SubscriptionAccess.IsInactive(company));
    }

    [Fact]
    public void BlankOrWhitespaceStatus_IsTreatedAsActive()
    {
        Assert.False(SubscriptionAccess.IsInactive(new Company
        {
            Plan = "accounting_pro",
            SubscriptionStatus = ""
        }));
        Assert.False(SubscriptionAccess.IsInactive(new Company
        {
            Plan = "accounting_pro",
            SubscriptionStatus = "   "
        }));
    }

    [Fact]
    public void PastDue_WithinGrace_IsNotInactive()
    {
        var company = new Company
        {
            Plan = "accounting_pro",
            SubscriptionStatus = "past_due",
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(-3)
        };
        Assert.False(SubscriptionAccess.IsInactive(company));
    }

    [Fact]
    public void PastDue_BeyondGrace_IsInactive()
    {
        var company = new Company
        {
            Plan = "accounting_pro",
            SubscriptionStatus = "past_due",
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(-20)
        };
        Assert.True(SubscriptionAccess.IsInactive(company));
    }

    [Fact]
    public void PastDue_UnspecifiedPeriodEnd_IsTreatedAsUtc()
    {
        // Unspecified kind must not be interpreted as local (UTC+7 would shrink grace by 7h).
        var periodEndUnspecified = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-13).AddHours(-20), DateTimeKind.Unspecified);
        var company = new Company
        {
            Plan = "accounting_pro",
            SubscriptionStatus = "past_due",
            CurrentPeriodEnd = periodEndUnspecified
        };
        Assert.False(SubscriptionAccess.IsInactive(company, utcNow: DateTime.UtcNow));
    }

    [Fact]
    public void Canceled_IsInactive()
    {
        var company = new Company
        {
            Plan = "accounting_pro",
            SubscriptionStatus = "canceled",
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(5)
        };
        Assert.True(SubscriptionAccess.IsInactive(company));
    }

    [Fact]
    public void Comp_IgnoresCanceledStatus()
    {
        var company = new Company
        {
            Plan = "enterprise",
            Tier = "comp",
            SubscriptionStatus = "canceled"
        };
        Assert.False(SubscriptionAccess.IsInactive(company));
        Assert.True(SubscriptionAccess.IsModuleAvailable(company, "inventory"));
    }

    [Fact]
    public void BillingManagedManually_IgnoresCanceledStatus()
    {
        var company = new Company
        {
            Plan = "accounting_pro",
            BillingManagedManually = true,
            SubscriptionStatus = "canceled"
        };
        Assert.False(SubscriptionAccess.IsInactive(company));
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("paused")]
    [InlineData("unpaid")]
    [InlineData("incomplete_expired")]
    [InlineData("weird_status")]
    public void TerminalAndUnknownStatuses_AreInactive(string status)
    {
        var company = new Company { Plan = "accounting_pro", SubscriptionStatus = status };
        Assert.True(SubscriptionAccess.IsInactive(company));
    }

    [Fact]
    public void CanceledEnterprise_KeepsFreeModules_BlocksInventory()
    {
        var company = new Company
        {
            Plan = "enterprise",
            SubscriptionStatus = "canceled",
            ActiveModulesJson = "[\"sales\",\"purchases\",\"financials\",\"reports\",\"inventory\",\"donations\",\"audit\"]"
        };
        Assert.True(SubscriptionAccess.IsModuleAvailable(company, "sales"));
        Assert.True(SubscriptionAccess.IsModuleAvailable(company, "financials"));
        Assert.False(SubscriptionAccess.IsModuleAvailable(company, "inventory"));
    }

    [Fact]
    public void InactivePlan_DoesNotRestoreOwnerOptedOutModule()
    {
        // accounting_free includes reports, but the owner turned it off while on Pro.
        var company = new Company
        {
            Plan = "accounting_pro",
            SubscriptionStatus = "canceled",
            ActiveModulesJson = "[\"sales\",\"purchases\",\"financials\"]"
        };
        Assert.True(company.HasModule("sales"));
        Assert.False(company.HasModule("reports"));
        Assert.True(SubscriptionAccess.IsModuleAvailable(company, "sales"));
        Assert.False(SubscriptionAccess.IsModuleAvailable(company, "reports"));
    }

    [Fact]
    public void LapsedPaidPlan_KeepsFreeRowModulesThatRemainEnabled()
    {
        // Integrity: free-row modules the owner still has enabled stay available after lapse
        // (creation quotas are a separate gate; this only covers module availability).
        var company = new Company
        {
            Plan = "accounting_pro",
            SubscriptionStatus = "canceled",
            Tier = null
        };
        Assert.True(SubscriptionAccess.IsModuleAvailable(company, "sales"));
        Assert.True(SubscriptionAccess.IsModuleAvailable(company, "reports"));
    }
}
