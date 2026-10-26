// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;

namespace Api.Service;

public sealed record SecurityCapabilities(
    bool MfaAvailable,
    bool UpgradeAvailable,
    bool Enrolled,
    IReadOnlyList<string> Methods);

public interface ISecurityCapabilities
{
    Task<SecurityCapabilities> GetAsync(ApplicationUser user, CancellationToken ct = default);
}

public class CommunitySecurityCapabilities : ISecurityCapabilities
{
    public Task<SecurityCapabilities> GetAsync(ApplicationUser user, CancellationToken ct = default)
    {
        return Task.FromResult(new SecurityCapabilities(
            MfaAvailable: false,
            UpgradeAvailable: false,
            Enrolled: false,
            Methods: Array.Empty<string>()));
    }
}
