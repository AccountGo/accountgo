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
    public void Integrity_QuotaDoesNotAffectModuleAvailability()
    {
        // Accounting integrity: module reads stay available for free-row modules even when
        // the paid subscription has lapsed (creation quotas are a separate gate).
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
