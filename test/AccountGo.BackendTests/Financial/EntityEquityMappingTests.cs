// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Core.Domain;
using Xunit;

namespace AccountGo.BackendTests.Financial;

public class EntityEquityMappingTests
{
    public static TheoryData<EntityType, string, string?, string, string> ProfileCases => new()
    {
        { EntityType.SoleProprietor, "30100", "30800", "30100", "Owner's Capital" },
        { EntityType.SoleProprietor, "30100", "30800", "30300", "Retained Earnings" },
        { EntityType.Partnership, "30100", "30800", "30100", "Partners' Capital" },
        { EntityType.Partnership, "30100", "30800", "30800", "Partners' Drawings" },
        { EntityType.Corporation, "30300", "30810", "30100", "Share Capital" },
        { EntityType.Corporation, "30300", "30810", "30300", "Retained Earnings" },
        { EntityType.Cooperative, "30300", null, "30100", "Member Capital" },
        { EntityType.Cooperative, "30300", null, "30300", "Retained Surplus" },
        { EntityType.NonProfit, "30300", null, "30300", "Net Assets" },
    };

    [Theory]
    [MemberData(nameof(ProfileCases))]
    public void GetProfile_maps_entity_type_to_equity_accounts_and_labels(
        EntityType entityType,
        string expectedCloseTarget,
        string? expectedDistribution,
        string displayCode,
        string expectedDisplayName)
    {
        var profile = EntityEquityMapping.GetProfile(entityType);

        Assert.Equal(expectedCloseTarget, profile.CloseTargetAccountCode);
        Assert.Equal(expectedDistribution, profile.DistributionAccountCode);
        Assert.True(profile.DisplayNames.TryGetValue(displayCode, out var name));
        Assert.Equal(expectedDisplayName, name);
    }

    [Theory]
    [InlineData("30100", "Share Capital", true)]
    [InlineData("30100", "My Custom Capital", false)]
    [InlineData("30300", "Retained Earnings", true)]
    [InlineData("30300", "Surplus Account", false)]
    [InlineData("30800", "Owner's Drawings", true)]
    [InlineData("30800", "Drawings", false)]
    [InlineData("99999", "Anything", false)]
    public void IsRenameable_only_allows_template_display_names(string code, string currentName, bool expected)
    {
        Assert.Equal(expected, EntityEquityMapping.IsRenameable(code, currentName));
    }
}
