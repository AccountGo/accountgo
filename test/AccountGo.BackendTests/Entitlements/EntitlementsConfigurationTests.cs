// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Collections.Generic;
using Api.Options;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AccountGo.BackendTests.Entitlements;

public class EntitlementsConfigurationTests
{
    [Fact]
    public void FromConfiguration_Defaults_MatchToday()
    {
        var options = Read(new Dictionary<string, string?>());
        Assert.False(options.Enabled);
        Assert.True(options.FeaturesEnabled);
        Assert.Null(options.SupportEmail);
    }

    [Fact]
    public void FromConfiguration_BindsThreeKeptSettings()
    {
        var options = Read(new Dictionary<string, string?>
        {
            ["ENTITLEMENTS_ENABLED"] = "true",
            ["ENTITLEMENT_FEATURES_ENABLED"] = "false",
            ["SUPPORT_EMAIL"] = "help@example.com",
        });
        Assert.True(options.Enabled);
        Assert.False(options.FeaturesEnabled);
        Assert.Equal("help@example.com", options.SupportEmail);
    }

    [Fact]
    public void FromConfiguration_SupportEmail_FallsBackToEmailSupportAddress()
    {
        var options = Read(new Dictionary<string, string?>
        {
            ["Email:SupportAddress"] = "fallback@example.com",
        });
        Assert.Equal("fallback@example.com", options.SupportEmail);
    }

    [Fact]
    public void FromConfiguration_DeletedVariables_AreIgnored()
    {
        var options = Read(new Dictionary<string, string?>
        {
            ["ENTITLEMENTS_ENABLED"] = "true",
            ["ENTITLEMENTS_SELF_SERVICE_PLANS"] = "invoicing,accounting",
            ["ENTITLEMENTS_FREE_SEATS"] = "1",
            ["ENTITLEMENTS_ENGINE_SHADOW"] = "false",
            ["ENTITLEMENTS_LEGACY_MODULE_GATE"] = "true",
        });
        Assert.True(options.Enabled);
        Assert.True(options.FeaturesEnabled);
    }

    private static Core.Entitlements.EntitlementsOptions Read(Dictionary<string, string?> values) =>
        EntitlementsConfiguration.FromConfiguration(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build());
}
