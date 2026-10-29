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
    public void FromConfiguration_Defaults_SupportEmailNull()
    {
        var options = Read(new Dictionary<string, string?>());
        Assert.Null(options.SupportEmail);
    }

    [Fact]
    public void FromConfiguration_BindsSupportEmail()
    {
        var options = Read(new Dictionary<string, string?>
        {
            ["SUPPORT_EMAIL"] = "help@example.com",
        });
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

    private static Core.Entitlements.EntitlementsOptions Read(Dictionary<string, string?> values) =>
        EntitlementsConfiguration.FromConfiguration(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build());
}
