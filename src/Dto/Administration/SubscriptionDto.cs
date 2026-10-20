using System;
using System.Collections.Generic;

namespace Dto.Subscription
{
    public class TenantSubscriptionDto
    {
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string? Slug { get; set; }
        public string Plan { get; set; } = "accounting";
        public string SubscriptionStatus { get; set; } = "active";
        public DateTime? SubscriptionValidUntil { get; set; }
        public List<string> ActiveModules { get; set; } = new();
    }

    public class UpdateSubscriptionDto
    {
        public int? CompanyId { get; set; }
        public string? Slug { get; set; }
        public string Plan { get; set; } = "invoicing";
        public List<string>? ActiveModules { get; set; }
    }

    public class PlanDefinitionDto
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> IncludedModules { get; set; } = new();
        public decimal MonthlyPrice { get; set; }
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
