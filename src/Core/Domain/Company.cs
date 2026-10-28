//-----------------------------------------------------------------------
// <copyright file="Company.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.Json;

namespace Core.Domain
{
    [Table("Company")]
    public partial class Company : BaseEntity
    {
        public string Name { get; set; } = string.Empty;
        public string ShortName { get; set; } = string.Empty;
        public string? Slug { get; set; }
        public string? CompanyCode { get; set; }
        public string? CRA { get; set; }

        public string? Plan { get; set; } = "accounting";
        /// <summary>Pricing tier within the plan: null/"free", "pro", or "comp" (see Core.Entitlements.CompanyTiers).</summary>
        public string? Tier { get; set; }
        public string? ActiveModulesJson { get; set; }
        public string? SubscriptionStatus { get; set; } = "active";
        public DateTime? SubscriptionValidUntil { get; set; }

        /// <summary>Billing backend: "stripe", "paddle", "manual", or null when community/self-hosted.</summary>
        public string? BillingProvider { get; set; }
        /// <summary>Provider customer id (e.g. cus_… / ctm_…).</summary>
        public string? BillingCustomerId { get; set; }
        /// <summary>Provider subscription id (e.g. sub_…).</summary>
        public string? BillingSubscriptionId { get; set; }
        /// <summary>End of the current paid period (UTC). Preferred over <see cref="SubscriptionValidUntil"/> when set.</summary>
        public DateTime? CurrentPeriodEnd { get; set; }
        public bool CancelAtPeriodEnd { get; set; }
        /// <summary>When true, provider webhooks must not overwrite plan/status (enterprise / ops-managed).</summary>
        public bool BillingManagedManually { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedByUserId { get; set; }
        public DateTime? DeletedAt { get; set; }

        public List<string> GetActiveModules()
        {
            var planModules = ModulesForPlan(Plan);
            List<string>? stored = null;
            if (!string.IsNullOrWhiteSpace(ActiveModulesJson))
            {
                try
                {
                    stored = JsonSerializer.Deserialize<List<string>>(ActiveModulesJson);
                }
                catch { }
            }

            if (stored == null || stored.Count == 0)
                return planModules;

            // Keep stored subset (Accounting can toggle modules), but always force
            // plan-required cores so legacy Invoicing tenants with ["sales"] get purchases.
            return planModules
                .Where(m => IsRequiredModule(Plan, m)
                    || stored.Contains(m, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>Community catalog plan keys (also used by GetPlanDefinitions). Product×tier keys are accepted via <see cref="IsKnownPlan"/>.</summary>
        public static readonly IReadOnlyList<string> KnownPlans = new[] { "invoicing", "accounting", "enterprise" };

        private static readonly HashSet<string> ProductTierPlans = new(StringComparer.OrdinalIgnoreCase)
        {
            "invoicing_free", "invoicing_pro", "accounting_free", "accounting_pro"
        };

        public static bool IsKnownPlan(string? plan) =>
            plan != null
            && (KnownPlans.Contains(plan.Trim(), StringComparer.OrdinalIgnoreCase)
                || ProductTierPlans.Contains(plan.Trim()));

        /// <summary>True when the plan key denotes a paid (self-serve or enterprise) product tier.</summary>
        public static bool IsPaidPlan(string? plan)
        {
            if (string.IsNullOrWhiteSpace(plan)) return false;
            var key = plan.Trim().ToLowerInvariant();
            return key is "invoicing_pro" or "accounting_pro" or "enterprise"
                || key.EndsWith("_pro", StringComparison.Ordinal);
        }

        /// <summary>
        /// Capability rank for upgrade ordering (higher = more capable).
        /// Ladder: Invoicing Free → Invoicing Pro → Accounting Free → Accounting Pro → Enterprise.
        /// Moving to a lower or equal rank is a downgrade / lateral change, not a self-serve upgrade.
        /// </summary>
        public static int PlanRank(string? plan)
        {
            var key = (plan ?? string.Empty).Trim().ToLowerInvariant();
            return key switch
            {
                "invoicing" or "invoicing_free" => 10,
                "invoicing_pro" => 20,
                "accounting" or "accounting_free" => 30,
                "accounting_pro" => 40,
                "enterprise" => 50,
                _ => 0
            };
        }

        /// <summary>True when <paramref name="toPlan"/> is strictly higher on the capability ladder than <paramref name="fromPlan"/>.</summary>
        public static bool IsUpgrade(string? fromPlan, string? toPlan) =>
            PlanRank(toPlan) > PlanRank(fromPlan);

        /// <summary>
        /// Self-serve plan changes (one plan at a time). Cancel of *_pro is Stripe Portal, not this method.
        /// <list type="bullet">
        /// <item><c>accounting_free</c> → <c>accounting_pro</c> or <c>invoicing_free</c></item>
        /// <item><c>invoicing_free</c> → <c>invoicing_pro</c> or <c>accounting_free</c></item>
        /// <item><c>invoicing_pro</c> → <c>accounting_pro</c> (upgrade product; Checkout / portal proration)</item>
        /// </list>
        /// Never both Pros, never Accounting Pro → Invoicing *, never free → other product's Pro in one step.
        /// </summary>
        public static bool IsAllowedSelfServicePlanChange(string? fromPlan, string? toPlan)
        {
            var from = NormalizePlanKey(fromPlan);
            var to = NormalizePlanKey(toPlan);
            if (string.IsNullOrEmpty(to) || string.Equals(from, to, StringComparison.Ordinal))
                return false;

            return (from, to) switch
            {
                ("accounting_free", "accounting_pro") => true,
                ("accounting_free", "invoicing_free") => true,
                ("invoicing_free", "invoicing_pro") => true,
                ("invoicing_free", "accounting_free") => true,
                ("invoicing_pro", "accounting_pro") => true,
                _ => false
            };
        }

        /// <summary>Same-product free landing after cancel (UI read-only; cancel itself is Stripe Portal).</summary>
        public static string CancelLandingPlan(string? plan) => FreePlanFor(plan);

        /// <summary>Normalize legacy community keys to product×tier keys used for ranking and change rules.</summary>
        public static string NormalizePlanKey(string? plan)
        {
            var key = (plan ?? string.Empty).Trim().ToLowerInvariant();
            return key switch
            {
                "invoicing" => "invoicing_free",
                "accounting" => "accounting_free",
                _ => key
            };
        }

        /// <summary>Maps a product×tier plan key to the free key of the same product (e.g. accounting_pro → accounting_free).</summary>
        public static string FreePlanFor(string? plan)
        {
            var key = NormalizePlanKey(plan ?? "accounting");
            if (key.StartsWith("invoicing", StringComparison.Ordinal))
                return "invoicing_free";
            if (key is "enterprise")
                return "accounting_free";
            return "accounting_free";
        }

        /// <summary>Modules included by subscription tier, ordered Invoicing → Accounting → Enterprise.</summary>
        public static List<string> ModulesForPlan(string? plan)
        {
            var key = plan?.ToLowerInvariant() ?? "accounting";
            if (key.StartsWith("invoicing", StringComparison.Ordinal))
                return new List<string> { "sales", "purchases" };
            if (key is "enterprise")
                return new List<string> { "sales", "financials", "purchases", "reports", "inventory", "donations", "audit" };
            return new List<string> { "sales", "financials", "purchases", "reports" };
        }

        /// <summary>
        /// Modules that cannot be turned off for a plan. Sales is always required;
        /// purchases is required on Invoicing (invoice + expense loop without full accounting).
        /// </summary>
        public static bool IsRequiredModule(string? plan, string moduleKey)
        {
            if (string.Equals(moduleKey, "sales", StringComparison.OrdinalIgnoreCase))
                return true;
            var key = plan?.ToLowerInvariant() ?? "";
            if (string.Equals(moduleKey, "purchases", StringComparison.OrdinalIgnoreCase)
                && key.StartsWith("invoicing", StringComparison.Ordinal))
                return true;
            return false;
        }

        public bool HasModule(string moduleKey)
        {
            var active = GetActiveModules();
            return active.Any(m => string.Equals(m, moduleKey, StringComparison.OrdinalIgnoreCase));
        }
    }
}
