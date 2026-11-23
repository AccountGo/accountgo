// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;

namespace Core.Domain;

/// <summary>
/// Persisted per-session pseudonym token mapping for assistant tool responses.
/// Maps an opaque token (e.g. <c>C-A1B2C3D4</c>) back to a workspace entity public id
/// scoped strictly to <c>(CompanyId, UserId, SessionId)</c>.
/// </summary>
public sealed class AssistantPseudonym : BaseEntity, ICompanyOwned
{
    public int CompanyId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string PublicId { get; set; } = string.Empty;
    /// <summary>
    /// Original display text stored ONLY for synthetic (<c>syn:</c>) entries that have no backing workspace entity,
    /// so the UI can re-identify them within the same <c>(CompanyId, UserId, SessionId)</c> scope.
    /// Always <c>null</c> for real workspace entities.
    /// </summary>
    public string? DisplayText { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastUsedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
