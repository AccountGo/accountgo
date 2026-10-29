// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Core.Entitlements;
using Microsoft.Extensions.Configuration;

namespace Api.Options
{
    /// <summary>
    /// Binds the support contact from flat environment-style keys.
    /// <code>
    /// SUPPORT_EMAIL=support@example.com
    /// </code>
    /// </summary>
    public static class EntitlementsConfiguration
    {
        public static EntitlementsOptions FromConfiguration(IConfiguration configuration)
        {
            return new EntitlementsOptions
            {
                SupportEmail = configuration["SUPPORT_EMAIL"] ?? configuration["Email:SupportAddress"],
            };
        }
    }
}
