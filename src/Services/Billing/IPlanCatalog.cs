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
    /// <summary>The final plan list, in display order, with <see cref="PlanDefinitionDto.SelfService"/> already set.</summary>
    IReadOnlyList<PlanDefinitionDto> GetPlans();
}
