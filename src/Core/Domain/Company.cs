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
using System.ComponentModel.DataAnnotations.Schema;

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

        public string? Plan { get; set; } = "accounting_free";
        /// <summary>Ops override only: null (derived from Plan) or "comp" (see Core.Entitlements.CompanyTiers).</summary>
        public string? Tier { get; set; }
        /// <summary>Legacy allow-list JSON. Prefer <see cref="DisabledModulesJson"/>. Kept for one-release read fallback.</summary>
        public string? ActiveModulesJson { get; set; }
        /// <summary>Owner opt-outs: plan modules the owner turned off. Null until backfill / first write.</summary>
        public string? DisabledModulesJson { get; set; }
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
        /// <summary>UTC stamp used by entitlement implementations that track paid→free transitions. Not written by the community engine.</summary>
        public DateTime? PaidAccessEndedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedByUserId { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}
