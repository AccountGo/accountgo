// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Collections.Generic;
using System.Linq;
using Core.Domain;
using Dto.Subscription;

namespace Services.Billing;

/// <summary>
/// Self-hosted / community default: one plan with every module. Owners turn modules off; there is
/// no plan to pick and no price.
/// </summary>
public sealed class CommunityPlanCatalog : IPlanCatalog
{
    public const string PlanKey = "community";

    /// <summary>Every module key, in the order of <c>GET /api/Subscription/modules</c>.</summary>
    public static readonly IReadOnlyList<string> AllModules = new[]
    {
        "sales", "financials", "purchases", "reports", "inventory", "donations", "audit"
    };

    public IReadOnlyList<PlanDefinitionDto> GetPlans() => new[]
    {
        new PlanDefinitionDto
        {
            Key = PlanKey,
            Name = "Community",
            Description = "Every module. Turn off the ones you don't need.",
            IncludedModules = AllModules.ToList(),
            RequiredModules = AllModules.Where(m => Company.IsRequiredModule(PlanKey, m)).ToList(),
            SelfService = false,
            MonthlyPrice = null
        }
    };
}
