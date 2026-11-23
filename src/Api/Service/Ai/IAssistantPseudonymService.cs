// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Api.Service.Ai;

public static class AssistantPseudonymKind
{
    public const string Customer = "customer";
    public const string Vendor = "vendor";
    public const string Person = "person";
    public const string BankAccount = "bank_account";

    public const string SyntheticKeyPrefix = "syn:";

    public static bool IsSyntheticPublicId(string? publicId) =>
        !string.IsNullOrEmpty(publicId) && publicId.StartsWith(SyntheticKeyPrefix, StringComparison.Ordinal);

    public static char RolePrefix(string kind) => kind switch
    {
        Customer => 'C',
        Vendor => 'V',
        Person => 'P',
        BankAccount => 'B',
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported pseudonym entity kind.")
    };
}

public sealed record AssistantPseudonymEntity(string Kind, string PublicId, string? DisplayText = null)
{
    public bool Equals(AssistantPseudonymEntity? other) =>
        other is not null
        && string.Equals(Kind, other.Kind, StringComparison.Ordinal)
        && string.Equals(PublicId, other.PublicId, StringComparison.Ordinal);

    public override int GetHashCode() =>
        HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(Kind),
            StringComparer.Ordinal.GetHashCode(PublicId));
}

public sealed record AssistantPseudonymResolution(string Token, string Kind, string PublicId, string? DisplayText = null);

public interface IAssistantPseudonymKeyProvider
{
    byte[] AppDerivedKey { get; }
}

public interface IAssistantPseudonymService
{
    /// <summary>
    /// Computes a keyed synthetic key (<c>syn:&lt;kind&gt;:&lt;hmac&gt;</c>) using the app-derived pseudonym key
    /// for party/person/bank names that do not have a persisted workspace entity public id.
    /// </summary>
    string BuildSyntheticKey(string kind, string rawValue);

    /// <summary>
    /// Computes or retrieves the deterministic per-session pseudonym token (e.g. <c>C-A1B2C3D4</c>,
    /// extended to 12 hex chars on per-session collision) and upserts it into <c>AssistantPseudonyms</c>
    /// with refreshed <c>LastUsedAt</c> and 30-day <c>ExpiresAt</c>.
    /// When <paramref name="sessionId"/> is null or invalid, uses a per-request ephemeral key
    /// and does not persist to the database.
    /// </summary>
    Task<string> PseudonymizeAsync(
        int companyId,
        string userId,
        string? sessionId,
        string kind,
        string publicId,
        CancellationToken cancellationToken = default,
        string? displayText = null);

    /// <summary>
    /// Batch version of <see cref="PseudonymizeAsync"/> that minimizes database round-trips.
    /// </summary>
    Task<IReadOnlyDictionary<AssistantPseudonymEntity, string>> PseudonymizeBatchAsync(
        int companyId,
        string userId,
        string? sessionId,
        IEnumerable<AssistantPseudonymEntity> entities,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a single pseudonym token strictly within <c>(companyId, userId, sessionId)</c>.
    /// Returns <c>null</c> when <paramref name="sessionId"/> is absent/invalid, the token format is invalid,
    /// the mapping does not exist in this scope, or it has expired.
    /// </summary>
    Task<AssistantPseudonymResolution?> ResolveTokenAsync(
        int companyId,
        string userId,
        string? sessionId,
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a batch of pseudonym tokens strictly within <c>(companyId, userId, sessionId)</c>.
    /// Unknown, cross-session, or expired tokens are omitted from the returned dictionary.
    /// </summary>
    Task<IReadOnlyDictionary<string, AssistantPseudonymResolution>> ResolveTokensAsync(
        int companyId,
        string userId,
        string sessionId,
        IEnumerable<string> tokens,
        CancellationToken cancellationToken = default);
}
