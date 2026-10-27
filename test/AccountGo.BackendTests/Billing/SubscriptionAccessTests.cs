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
}
