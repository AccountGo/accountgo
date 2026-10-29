// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Collections.Generic;
using System.Linq;
using Dto.Subscription;

namespace Services.Billing;

/// <summary>
/// Self-hosted / community default: one plan with every module. Owners turn modules off; there is
/// no plan to pick and no price. Every key (including null/blank/unknown) resolves to community.
/// </summary>
public sealed class CommunityPlanCatalog : IPlanCatalog
{
    public const string PlanKey = "community";

    /// <inheritdoc />
    public string DefaultPlanKey => PlanKey;

    /// <summary>Every module key, in the order of <c>GET /api/Subscription/modules</c>.</summary>
    public static readonly IReadOnlyList<string> AllModules = new[]
    {
        "sales", "financials", "purchases", "reports", "inventory", "donations", "audit"
    };

    private static readonly PlanDefinitionDto CommunityPlan = new()
    {
        Key = PlanKey,
        Name = "Community",
        Description = "Every module. Turn off the ones you don't need.",
        IncludedModules = AllModules.ToList(),
        RequiredModules = new List<string> { "sales" },
        SelfService = false,
        MonthlyPrice = null
    };

    public IReadOnlyList<PlanDefinitionDto> GetPlans() => new[] { Clone(CommunityPlan) };

    public bool TryResolvePlan(string? key, out PlanDefinitionDto plan)
    {
        // Self-host never rejects a plan key — SPA may still send legacy product keys.
        plan = Clone(CommunityPlan);
        return true;
    }

    public PlanDefinitionDto ResolveStoredPlan(string? storedPlan) => Clone(CommunityPlan);

    private static PlanDefinitionDto Clone(PlanDefinitionDto source) => new()
    {
        Key = source.Key,
        Name = source.Name,
        Description = source.Description,
        IncludedModules = source.IncludedModules.ToList(),
        RequiredModules = source.RequiredModules.ToList(),
        SelfService = source.SelfService,
        MonthlyPrice = source.MonthlyPrice
    };
}
