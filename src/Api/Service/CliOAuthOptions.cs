// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Generic;

namespace Api.Service;

public sealed class CliOAuthOptions
{
    public const string SectionName = "CliOAuth";

    /// <summary>JWT audience for CLI access tokens.</summary>
    public string Audience { get; set; } = "cli";

    public string ClientId { get; set; } = "accountgo-cli";

    public string ClientDisplayName { get; set; } = "AccountGo CLI";

    /// <summary>Pending consent request lifetime (before approve).</summary>
    public TimeSpan PendingRequestTtl { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Authorization code lifetime after approve.</summary>
    public TimeSpan AuthorizationCodeTtl { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Access-token lifetime for CLI.</summary>
    public TimeSpan AccessTokenTtl { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Sliding idle window for CLI refresh sessions.</summary>
    public TimeSpan RefreshIdleTtl { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Absolute max lifetime for a CLI refresh family.</summary>
    public TimeSpan RefreshAbsoluteTtl { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Max age of MFA / auth time on the approving web session.</summary>
    public TimeSpan ConsentFreshness { get; set; } = TimeSpan.FromMinutes(15);

    public IReadOnlyList<string> AllowedScopes { get; set; } = new[] { "offline_access", "api" };

    public IReadOnlyList<string> RequiredScopes { get; set; } = new[] { "offline_access", "api" };
}
