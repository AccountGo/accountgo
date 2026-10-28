using System;
using System.Collections.Generic;

namespace Dto.Subscription
{
    public class TenantSubscriptionDto
    {
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string? Slug { get; set; }
        public string Plan { get; set; } = "accounting_free";
        public string SubscriptionStatus { get; set; } = "active";
        public DateTime? SubscriptionValidUntil { get; set; }
        public List<string> ActiveModules { get; set; } = new();
    }

    public class UpdateSubscriptionDto
    {
        public int? CompanyId { get; set; }
        public string? Slug { get; set; }
        public string? Plan { get; set; }
        public List<string>? ActiveModules { get; set; }
        /// <summary>Billing interval when requesting a paid plan change: "month" or "year". Ignored when billing is off.</summary>
        public string? BillingInterval { get; set; }
    }

    public class PlanDefinitionDto
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> IncludedModules { get; set; } = new();
        /// <summary>Included modules that cannot be switched off on this plan.</summary>
        public List<string> RequiredModules { get; set; } = new();
        /// <summary>Whether users may pick this plan themselves (false = assigned by the operator).</summary>
        public bool SelfService { get; set; } = true;
        /// <summary>Null when the plan has no price.</summary>
        public decimal? MonthlyPrice { get; set; }
    }

    public class ModuleDefinitionDto
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public bool IsCore { get; set; }
    }
}
