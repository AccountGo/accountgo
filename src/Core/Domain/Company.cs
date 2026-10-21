//-----------------------------------------------------------------------
// <copyright file="Company.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
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

        public List<string> GetActiveModules()
        {
            if (!string.IsNullOrWhiteSpace(ActiveModulesJson))
            {
                try
                {
                    var list = JsonSerializer.Deserialize<List<string>>(ActiveModulesJson);
                    if (list != null && list.Count > 0)
                        return list;
                }
                catch { }
            }

            return (Plan?.ToLowerInvariant()) switch
            {
                "invoicing" => new List<string> { "sales" },
                "enterprise" => new List<string> { "sales", "financials", "purchases", "inventory", "reports", "donations", "audit" },
                _ => new List<string> { "sales", "financials", "purchases", "reports" }
            };
        }

        public bool HasModule(string moduleKey)
        {
            var active = GetActiveModules();
            return active.Any(m => string.Equals(m, moduleKey, StringComparison.OrdinalIgnoreCase));
        }
    }
}
