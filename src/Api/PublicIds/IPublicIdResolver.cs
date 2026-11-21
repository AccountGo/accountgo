// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Api.PublicIds
{
    public enum PublicIdLookupError
    {
        None = 0,
        Malformed = 1,
        WrongKind = 2,
        NotFound = 3,
    }

    public readonly record struct PublicIdLookupResult(
        bool Ok,
        int Id,
        PublicIdLookupError Error,
        string? ExpectedPrefix = null)
    {
        public static PublicIdLookupResult Success(int id) => new(true, id, PublicIdLookupError.None);
        public static PublicIdLookupResult Fail(PublicIdLookupError error, string? expectedPrefix = null) =>
            new(false, 0, error, expectedPrefix);
    }

    /// <summary>
    /// Request-scoped translator between integer database keys and prefixed public IDs.
    /// Cache lifetime is strictly per request (never process-wide).
    /// </summary>
    public interface IPublicIdResolver
    {
        /// <summary>Returns the public ID for <paramref name="id"/>, or null if <paramref name="id"/> &lt;= 0 or not found.</summary>
        string? ToPublic(string prefix, int id);

        /// <summary>Returns the public ID for <paramref name="id"/>, throwing if <paramref name="id"/> &gt; 0 and no row exists.</summary>
        string? ToPublicOrThrow(string prefix, int id);

        /// <summary>Batches int-to-public lookups into one query per prefix (chunked at 1,000) into the per-request cache.</summary>
        Task PrefetchAsync(IEnumerable<(string Prefix, int Id)> items, CancellationToken cancellationToken = default);

        /// <summary>Resolves a public ID string to an integer key under the active tenant/user scope.</summary>
        PublicIdLookupResult Resolve(string? publicId, string expectedPrefix);

        /// <summary>Async twin of <see cref="Resolve"/> for MCP tools and async handlers.</summary>
        Task<PublicIdLookupResult> ResolveAsync(string? publicId, string expectedPrefix, CancellationToken cancellationToken = default);

        /// <summary>Resolves a polymorphic reference by type name to its public ID string, or null for exempt types.</summary>
        string? ToPublicPolymorphic(string? typeName, int id);

        /// <summary>Resolves a polymorphic public ID string using the given type name.</summary>
        PublicIdLookupResult ResolvePolymorphic(string? publicId, string? typeName);
    }
}
