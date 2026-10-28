// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Entitlements;
using Microsoft.Extensions.Configuration;

namespace Api.Options
{
    /// <summary>
    /// Reads free-tier settings from flat environment-style keys. None of them set = limits off (self-host default).
    /// <code>
    /// ENTITLEMENTS_ENABLED=true
    /// ENTITLEMENT_FEATURES_ENABLED=true
    /// ENTITLEMENTS_SELF_SERVICE_PLANS=invoicing,accounting
    /// ENTITLEMENTS_FREE_SALES_DOCUMENTS_PER_MONTH=5
    /// ENTITLEMENTS_FREE_BILLS_PER_MONTH=5
    /// ENTITLEMENTS_FREE_SEATS=1
    /// ENTITLEMENTS_FREE_BANK_ACCOUNTS=2
    /// ENTITLEMENTS_FREE_OWNED_WORKSPACES=1
    /// </code>
    /// <para>
    /// Kill switches (§8.1): <c>ENTITLEMENTS_ENABLED=false</c> makes quotas unlimited and keeps every plan
    /// self-service (self-host depends on this); it does not change modules, lifecycle, ReadOnlyModules, or features.
    /// <c>ENTITLEMENT_FEATURES_ENABLED=false</c> empties Features only.
    /// </para>
    /// </summary>
    public static class EntitlementsConfiguration
    {
        public static readonly IReadOnlyDictionary<MeteredResource, string> LimitKeys = new Dictionary<MeteredResource, string>
        {
            [MeteredResource.SalesDocuments] = "ENTITLEMENTS_FREE_SALES_DOCUMENTS_PER_MONTH",
            [MeteredResource.Bills] = "ENTITLEMENTS_FREE_BILLS_PER_MONTH",
            [MeteredResource.Seats] = "ENTITLEMENTS_FREE_SEATS",
            [MeteredResource.BankAccounts] = "ENTITLEMENTS_FREE_BANK_ACCOUNTS",
            [MeteredResource.OwnedWorkspaces] = "ENTITLEMENTS_FREE_OWNED_WORKSPACES",
        };

        public static EntitlementsOptions FromConfiguration(IConfiguration configuration)
        {
            var options = new EntitlementsOptions
            {
                Enabled = bool.TryParse(configuration["ENTITLEMENTS_ENABLED"], out var enabled) && enabled,
                // Default on: missing or non-false keeps feature emission enabled (community still emits none).
                FeaturesEnabled = !string.Equals(configuration["ENTITLEMENT_FEATURES_ENABLED"], "false", StringComparison.OrdinalIgnoreCase),
                SupportEmail = configuration["SUPPORT_EMAIL"] ?? configuration["Email:SupportAddress"],
                // Default on: missing or non-false keeps shadow mode when LegacyModuleGate is true.
                EngineShadow = !string.Equals(configuration["ENTITLEMENTS_ENGINE_SHADOW"], "false", StringComparison.OrdinalIgnoreCase),
                // Default off: engine owns module gates (E1b). Set true to restore E0.5 path + shadow.
                LegacyModuleGate = string.Equals(configuration["ENTITLEMENTS_LEGACY_MODULE_GATE"], "true", StringComparison.OrdinalIgnoreCase),
            };

            var plans = configuration["ENTITLEMENTS_SELF_SERVICE_PLANS"];
            if (!string.IsNullOrWhiteSpace(plans))
                options.SelfServicePlans = plans.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(p => p.ToLowerInvariant())
                    .Distinct()
                    .ToList();

            foreach (var (resource, key) in LimitKeys)
            {
                var raw = configuration[key];
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                if (!int.TryParse(raw, out var limit) || limit < 0)
                    throw new InvalidOperationException($"{key} must be a whole number of 0 or more (got '{raw}').");
                options.FreeLimits[resource] = limit;
            }
            return options;
        }
    }
}
