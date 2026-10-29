// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Core.Entitlements;
using Microsoft.Extensions.Configuration;

namespace Api.Options
{
    /// <summary>
    /// Binds entitlement switches from flat environment-style keys.
    /// Other implementations may read additional variables; the community engine ignores
    /// <see cref="EntitlementsOptions.Enabled"/> and <see cref="EntitlementsOptions.FeaturesEnabled"/>
    /// for gates.
    /// <code>
    /// ENTITLEMENTS_ENABLED=true
    /// ENTITLEMENT_FEATURES_ENABLED=true
    /// SUPPORT_EMAIL=support@example.com
    /// </code>
    /// </summary>
    public static class EntitlementsConfiguration
    {
        public static EntitlementsOptions FromConfiguration(IConfiguration configuration)
        {
            return new EntitlementsOptions
            {
                Enabled = bool.TryParse(configuration["ENTITLEMENTS_ENABLED"], out var enabled) && enabled,
                // Default on: missing or non-false keeps the switch enabled for other implementations.
                FeaturesEnabled = !string.Equals(configuration["ENTITLEMENT_FEATURES_ENABLED"], "false", StringComparison.OrdinalIgnoreCase),
                SupportEmail = configuration["SUPPORT_EMAIL"] ?? configuration["Email:SupportAddress"],
            };
        }
    }
}
