// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Services.Security;

namespace Api.Service;

/// <summary>
/// Single async loader for a user's combined ASP.NET Identity and domain security roles,
/// shared by token minting and session user DTO construction so the two cannot drift.
/// </summary>
internal static class UserRoleResolver
{
    public static async Task<(IReadOnlyList<string> Roles, User? DomainUser)> LoadAsync(
        UserManager<ApplicationUser> userManager,
        ISecurityService securityService,
        ApplicationUser user)
    {
        var identityRoles = await userManager.GetRolesAsync(user);
        var roles = new List<string>(identityRoles.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in identityRoles)
        {
            if (!string.IsNullOrEmpty(role) && seen.Add(role))
                roles.Add(role);
        }

        var domainUser = await securityService.GetUserAsync(user.UserName ?? user.Email ?? string.Empty);
        if (domainUser?.Roles != null)
        {
            foreach (var r in domainUser.Roles)
            {
                var name = r?.SecurityRole?.Name;
                if (!string.IsNullOrEmpty(name) && seen.Add(name))
                    roles.Add(name);
            }
        }

        return (roles, domainUser);
    }
}
