// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;

namespace Core.Domain
{
    public record EntityEquityProfile(
        string CloseTargetAccountCode,
        string? DistributionAccountCode,
        IReadOnlyDictionary<string, string> DisplayNames);

    public static class EntityEquityMapping
    {
        private static readonly HashSet<string> RenameableCapitalNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Member Capital",
            "Owner's Capital",
            "Partners' Capital",
            "Share Capital"
        };

        private static readonly HashSet<string> RenameableRetainedSurplusNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Retained Surplus",
            "Retained Earnings",
            "Net Assets"
        };

        private static readonly HashSet<string> RenameableDrawingsNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Owner's Drawings",
            "Partners' Drawings"
        };

        private static readonly Dictionary<EntityType, EntityEquityProfile> Profiles = new()
        {
            [EntityType.SoleProprietor] = new EntityEquityProfile(
                "30100",
                "30800",
                new Dictionary<string, string>
                {
                    ["30100"] = "Owner's Capital",
                    ["30300"] = "Retained Earnings"
                }),

            [EntityType.Partnership] = new EntityEquityProfile(
                "30100",
                "30800",
                new Dictionary<string, string>
                {
                    ["30100"] = "Partners' Capital",
                    ["30800"] = "Partners' Drawings"
                }),

            [EntityType.Corporation] = new EntityEquityProfile(
                "30300",
                "30810",
                new Dictionary<string, string>
                {
                    ["30100"] = "Share Capital",
                    ["30300"] = "Retained Earnings"
                }),

            [EntityType.Cooperative] = new EntityEquityProfile(
                "30300",
                null,
                new Dictionary<string, string>
                {
                    ["30100"] = "Member Capital",
                    ["30300"] = "Retained Surplus"
                }),

            [EntityType.NonProfit] = new EntityEquityProfile(
                "30300",
                null,
                new Dictionary<string, string>
                {
                    ["30300"] = "Net Assets"
                })
        };

        public static EntityEquityProfile GetProfile(EntityType entityType)
        {
            if (Profiles.TryGetValue(entityType, out var profile))
                return profile;

            return Profiles[EntityType.Corporation];
        }

        public static bool IsRenameable(string accountCode, string currentAccountName)
        {
            if (string.IsNullOrWhiteSpace(currentAccountName))
                return false;

            return accountCode switch
            {
                "30100" => RenameableCapitalNames.Contains(currentAccountName),
                "30300" => RenameableRetainedSurplusNames.Contains(currentAccountName),
                "30800" => RenameableDrawingsNames.Contains(currentAccountName),
                _ => false
            };
        }
    }
}
