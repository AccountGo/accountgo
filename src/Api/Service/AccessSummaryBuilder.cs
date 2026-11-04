// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Api.Service
{
    /// <summary>One functional area an invitee will be able to use, with the strongest level granted.</summary>
    public sealed record AccessAreaSummary(string Area, string Label, string Level);

    /// <summary>
    /// Describes functional permissions in plain language (area + strongest level) so owners and invitees
    /// see what an access preset actually grants. Purely descriptive; it never grants or checks access.
    /// </summary>
    public static class AccessSummaryBuilder
    {
        public const string ViewOnlyLevel = "View only";
        public const string CreateAndEditLevel = "Create and edit";
        public const string ManageAndPostLevel = "Manage and post";

        private const char PermissionSeparator = '.';
        private static readonly string[] ManageActionSuffixes = { "manage", "post", "close", "reconcile", "undo" };
        private const string CreateActionSuffix = "create";

        private static readonly IReadOnlyDictionary<string, string> AreaLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sales"] = "Sales",
            ["financials"] = "Accounting",
            ["banking"] = "Banking",
            ["purchases"] = "Purchases",
            ["inventory"] = "Inventory",
            ["reports"] = "Reports",
            ["donations"] = "Donations",
            ["audit"] = "Audit log",
            ["admin"] = "Administration"
        };

        public static IReadOnlyList<AccessAreaSummary> Describe(IEnumerable<string> permissionNames)
        {
            if (permissionNames == null)
                throw new ArgumentNullException(nameof(permissionNames));

            return permissionNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .GroupBy(name => name.Split(PermissionSeparator)[0], StringComparer.OrdinalIgnoreCase)
                .Select(group => new AccessAreaSummary(
                    group.Key.ToLowerInvariant(),
                    AreaLabels.TryGetValue(group.Key, out var label) ? label : group.Key,
                    StrongestLevel(group)))
                .OrderBy(summary => summary.Label, StringComparer.Ordinal)
                .ToList();
        }

        private static string StrongestLevel(IEnumerable<string> permissionNames)
        {
            var level = ViewOnlyLevel;
            foreach (var name in permissionNames)
            {
                var action = name.Split(PermissionSeparator).Last();
                if (ManageActionSuffixes.Contains(action, StringComparer.OrdinalIgnoreCase))
                    return ManageAndPostLevel;
                if (string.Equals(action, CreateActionSuffix, StringComparison.OrdinalIgnoreCase))
                    level = CreateAndEditLevel;
            }
            return level;
        }
    }
}
