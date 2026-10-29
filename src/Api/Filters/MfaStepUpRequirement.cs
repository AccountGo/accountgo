// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace Api.Filters;

public class MfaStepUpRequirement : IAuthorizationRequirement
{
    public int MaxAgeSeconds { get; }

    public MfaStepUpRequirement(int maxAgeSeconds = 900) // 15 minutes default
    {
        MaxAgeSeconds = maxAgeSeconds;
    }
}

/// <summary>
/// Community / self-hosted step-up handler.
/// Since community builds do not enforce Factor B, sensitive actions are never blocked on community builds (F3).
/// A module may replace this handler (F2).
/// </summary>
public class CommunityStepUpHandler : AuthorizationHandler<MfaStepUpRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, MfaStepUpRequirement requirement)
    {
        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
