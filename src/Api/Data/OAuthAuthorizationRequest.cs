// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;

namespace Api.Data;

/// <summary>
/// Pending or issued OAuth authorization-code request for public CLI clients (PKCE).
/// The plaintext authorization code is never stored — only <see cref="CodeHash"/>.
/// </summary>
public class OAuthAuthorizationRequest
{
    public string Id { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string CodeChallenge { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? UserId { get; set; }
    /// <summary>Optional device label captured at consent Approve (shown in active sessions).</summary>
    public string? DeviceLabel { get; set; }
    /// <summary>SHA-256 (Base64) of the one-time authorization code. Null until the user approves.</summary>
    public string? CodeHash { get; set; }
    /// <summary>Refresh-token family minted at code exchange; used to revoke on code replay.</summary>
    public string? IssuedRefreshFamilyId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime? DeniedAt { get; set; }
}
