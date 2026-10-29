// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Collections.Generic;
using Dto.Subscription;

namespace Services.Billing;

/// <summary>
/// Plans served by <c>GET /api/Subscription/plans</c>. The community default registers one plan;
/// a module may replace this registration.
/// </summary>
public interface IPlanCatalog
{
    /// <summary>Plan key stored on new workspaces when the client omits one.</summary>
    string DefaultPlanKey { get; }

    /// <summary>The final plan list, in display order, with <see cref="PlanDefinitionDto.SelfService"/> already set.</summary>
    IReadOnlyList<PlanDefinitionDto> GetPlans();

    /// <summary>
    /// Validates a client-supplied plan key for create / update.
    /// When true, <paramref name="plan"/> is the catalog entry to store.
    /// </summary>
    bool TryResolvePlan(string? key, out PlanDefinitionDto plan);

    /// <summary>
    /// Resolves a stored company plan key for module allow-lists. Never fails —
    /// the catalog owns aliases and fallbacks.
    /// </summary>
    PlanDefinitionDto ResolveStoredPlan(string? storedPlan);
}
