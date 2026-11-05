// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Security.Claims;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Billing;

namespace Api.Controllers;

/// <summary>User-scoped entitlement helpers that do not require a workspace tenant header.</summary>
[ApiController]
[Authorize]
[TenantFree]
[Route("api/entitlements")]
public sealed class UserEntitlementsController : BaseController
{
    private readonly IEntitlementService _entitlements;
    private readonly IPlanCatalog _plans;

    public UserEntitlementsController(IEntitlementService entitlements, IPlanCatalog plans)
    {
        _entitlements = entitlements;
        _plans = plans;
    }

    /// <summary>Free-workspace slot usage for the signed-in user.</summary>
    [HttpGet("me")]
    public IActionResult GetMe()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var decision = _entitlements.EvaluateForUser(
            userId, new UserAction.CreateWorkspace(_plans.DefaultPlanKey));
        return Ok(new
        {
            freeWorkspace = new
            {
                used = decision.Used ?? 0,
                limit = decision.Limit
            }
        });
    }
}
