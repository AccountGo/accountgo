// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Core.Domain;
using Core.Entitlements;

namespace Core.Billing
{
    /// <summary>
    /// Shared rules for whether a workspace subscription still grants paid-module access.
    /// Grace matches the processor dunning window (14 days after period end while past_due).
    /// Precedence matches entitlement-engine-architecture.md §6.2 step 3 (comp / manual first).
    /// </summary>
    public static class SubscriptionAccess
    {
        public static readonly TimeSpan PastDueGrace = TimeSpan.FromDays(14);

        /// <summary>
        /// True when paid-plan modules must fall back to the same-product free row.
        /// Comp and manually billed workspaces are never inactive for access purposes.
        /// </summary>
        public static bool IsInactive(Company company, DateTime? utcNow = null)
        {
            ArgumentNullException.ThrowIfNull(company);

            if (CompanyTiers.Normalize(company.Tier) == CompanyTiers.Comp || company.BillingManagedManually)
                return false;

            var now = utcNow ?? DateTime.UtcNow;
            var status = string.IsNullOrWhiteSpace(company.SubscriptionStatus)
                ? "active"
                : company.SubscriptionStatus.Trim().ToLowerInvariant();

            if (status is "active" or "trialing")
                return false;

            if (status == "past_due")
            {
                var periodEnd = company.CurrentPeriodEnd ?? company.SubscriptionValidUntil;
                if (periodEnd == null)
                    return true;

                // D1/SQLite returns Unspecified; treat as UTC (not local) so grace is timezone-stable.
                var periodEndUtc = periodEnd.Value.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(periodEnd.Value, DateTimeKind.Utc)
                    : periodEnd.Value.ToUniversalTime();

                return now > periodEndUtc.Add(PastDueGrace);
            }

            // canceled, unpaid, incomplete, incomplete_expired, paused, unrecognized
            return true;
        }

        /// <summary>
        /// Whether <paramref name="moduleKey"/> is available under the current subscription lifecycle.
        /// When a paid plan is inactive, only modules that are both on the free plan row and still
        /// enabled on the company (owner opt-outs via <see cref="Company.HasModule"/>) remain.
        /// </summary>
        public static bool IsModuleAvailable(Company company, string moduleKey, DateTime? utcNow = null)
        {
            ArgumentNullException.ThrowIfNull(company);
            if (string.IsNullOrWhiteSpace(moduleKey))
                return false;

            if (Company.IsPaidPlan(company.Plan) && IsInactive(company, utcNow))
            {
                var freeModules = Company.ModulesForPlan(Company.FreePlanFor(company.Plan));
                return freeModules.Contains(moduleKey, StringComparer.OrdinalIgnoreCase)
                    && company.HasModule(moduleKey);
            }

            return company.HasModule(moduleKey);
        }
    }
}
