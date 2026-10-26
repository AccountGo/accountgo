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
        public string? ActiveModulesJson { get; set; }
        public string? SubscriptionStatus { get; set; } = "active";
        public DateTime? SubscriptionValidUntil { get; set; }
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

        /// <summary>Modules included by subscription tier, ordered Invoicing → Accounting → Enterprise.</summary>
        public static List<string> ModulesForPlan(string? plan) => (plan?.ToLowerInvariant()) switch
        {
            "invoicing" => new List<string> { "sales", "purchases" },
            "enterprise" => new List<string> { "sales", "financials", "purchases", "reports", "inventory", "donations", "audit" },
            _ => new List<string> { "sales", "financials", "purchases", "reports" }
        };

        /// <summary>
        /// Modules that cannot be turned off for a plan. Sales is always required;
        /// purchases is required on Invoicing (invoice + expense loop without full accounting).
        /// </summary>
        public static bool IsRequiredModule(string? plan, string moduleKey)
        {
            if (string.Equals(moduleKey, "sales", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(moduleKey, "purchases", StringComparison.OrdinalIgnoreCase)
                && string.Equals(plan, "invoicing", StringComparison.OrdinalIgnoreCase))
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
