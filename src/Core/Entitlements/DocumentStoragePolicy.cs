// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable

namespace Core.Entitlements
{
    /// <summary>
    /// Storage and per-file limits for stored documents in one workspace. <see cref="StorageLimitBytes"/> null means no storage
    /// cap. The <c>Library</c> limits apply to uploads made in the documents library (no link, or a customer/vendor link); the
    /// <c>Record</c> limits apply to files attached while uploading to a bill or invoice. Scanned captures keep their own limits.
    /// </summary>
    public sealed record DocumentStoragePolicy(
        long? StorageLimitBytes,
        int LibraryMaxFileBytes,
        int LibraryMaxPdfPages,
        int RecordMaxFileBytes,
        int RecordMaxPdfPages)
    {
        public const int BaseMaxFileBytes = 8 * 1024 * 1024;
        public const int BaseMaxPdfPages = 10;
        public const int StoredMaxFileBytes = 15 * 1024 * 1024;
        public const int StoredMaxPdfPages = 300;

        /// <summary>Self-hosted community edition: no storage cap, larger library files.</summary>
        public static DocumentStoragePolicy Community { get; } =
            new(null, StoredMaxFileBytes, StoredMaxPdfPages, BaseMaxFileBytes, BaseMaxPdfPages);

        /// <summary>
        /// Fail-safe default for an entitlement engine that does not define document storage: base file limits and no storage,
        /// so nothing can be stored until the engine grants it.
        /// </summary>
        public static DocumentStoragePolicy NotGranted { get; } =
            new(0, BaseMaxFileBytes, BaseMaxPdfPages, BaseMaxFileBytes, BaseMaxPdfPages);

        /// <summary>Clamps every value to the supported range so a misconfigured engine can never exceed the platform maximum.</summary>
        public DocumentStoragePolicy Clamp() => new(
            StorageLimitBytes is { } limit ? System.Math.Max(0, limit) : null,
            System.Math.Clamp(LibraryMaxFileBytes, 1, StoredMaxFileBytes),
            System.Math.Clamp(LibraryMaxPdfPages, 1, StoredMaxPdfPages),
            System.Math.Clamp(RecordMaxFileBytes, 1, BaseMaxFileBytes),
            System.Math.Clamp(RecordMaxPdfPages, 1, BaseMaxPdfPages));
    }
}
