// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable

namespace Api.Options;

/// <summary>
/// Minimum retention for security and data-access events. Configuration may lengthen periods,
/// never shorten them below the floors.
/// </summary>
public sealed class AuditRetentionOptions
{
    public const string SectionName = "AuditRetention";

    /// <summary>Floor for non-data-access security events (days).</summary>
    public const int SecurityEventFloorDays = 730;

    /// <summary>Floor for <c>data.*</c> and <c>invoice.pdf.public</c> events (days).</summary>
    public const int DataAccessFloorDays = 365;

    /// <summary>Fixed Postgres advisory-lock key so parallel API instances do not purge concurrently.</summary>
    public const long PostgresAdvisoryLockKey = 872_650_314L;

    public const int DeleteBatchSize = 5_000;

    public bool Enabled { get; set; } = true;

    /// <summary>Days to keep security events (raised to <see cref="SecurityEventFloorDays"/> if lower).</summary>
    public int SecurityEventDays { get; set; } = SecurityEventFloorDays;

    /// <summary>Days to keep data-access events (raised to <see cref="DataAccessFloorDays"/> if lower).</summary>
    public int DataAccessDays { get; set; } = DataAccessFloorDays;

    public static bool IsDataAccessEvent(string eventType) =>
        eventType.StartsWith("data.", System.StringComparison.Ordinal)
        || string.Equals(eventType, "invoice.pdf.public", System.StringComparison.Ordinal);
}
