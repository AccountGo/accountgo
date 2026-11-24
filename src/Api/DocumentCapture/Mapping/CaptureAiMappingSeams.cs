// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.Data;
using Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Api.DocumentCaptures.Mapping;

/// <summary>The notice wording an owner agrees to. Change <see cref="CurrentVersion"/> with the wording so owners are asked again.</summary>
public static class CaptureAiMappingConsentNotice
{
    public const string CurrentVersion = "2026-11-24";
}

/// <summary>The closed set of reasons a workspace cannot use AI field mapping. Null means it can.</summary>
public static class CaptureAiMappingUnavailableReasons
{
    public const string NotOffered = "not_offered";
    public const string Plan = "plan";
    public const string TurnedOffBySupport = "turned_off_by_support";
    public const string CapNotSet = "cap_not_set";

    public static bool IsKnown(string? reason) =>
        reason is NotOffered or Plan or TurnedOffBySupport or CapNotSet;
}

/// <summary>Whether a host offers AI field mapping to a workspace. Consent is not required for it to be offered.</summary>
public sealed record CaptureAiMappingAvailability(bool Available, string? UnavailableReason)
{
    public static CaptureAiMappingAvailability Offered { get; } = new(true, null);
    public static CaptureAiMappingAvailability Unavailable(string reason) => new(false, reason);
}

public interface ICaptureAiMappingAvailability
{
    Task<CaptureAiMappingAvailability> GetAsync(int companyId, CancellationToken cancellationToken);
}

/// <summary>The default: nothing is offered, so the setting stays hidden and the feature stays off.</summary>
public sealed class NotOfferedCaptureAiMappingAvailability : ICaptureAiMappingAvailability
{
    public Task<CaptureAiMappingAvailability> GetAsync(int companyId, CancellationToken cancellationToken) =>
        Task.FromResult(CaptureAiMappingAvailability.Unavailable(CaptureAiMappingUnavailableReasons.NotOffered));
}

/// <summary>A workspace's consent as stored. <see cref="IsCurrent"/> is false once revoked or when the notice wording has changed since.</summary>
public sealed record CaptureAiMappingConsentRecord(string ConsentVersion, DateTime ConsentedAt, string ConsentedByUserId, DateTime? RevokedAt)
{
    public bool IsCurrent => RevokedAt is null && string.Equals(ConsentVersion, CaptureAiMappingConsentNotice.CurrentVersion, StringComparison.Ordinal);
    public bool NeedsReconsent => RevokedAt is null && !IsCurrent;
}

public interface ICaptureAiMappingConsentStore
{
    /// <summary>Reads a workspace's consent by explicit company id. Safe off-request: the host policy and the background worker have no tenant.</summary>
    Task<CaptureAiMappingConsentRecord?> GetAsync(int companyId, CancellationToken cancellationToken);

    /// <summary>Records consent to the current notice. Returns false (and changes nothing) when current consent already exists.</summary>
    Task<bool> GrantAsync(int companyId, string userId, CancellationToken cancellationToken);

    /// <summary>Revokes consent. Returns false (and changes nothing) when there is no active consent.</summary>
    Task<bool> RevokeAsync(int companyId, string userId, CancellationToken cancellationToken);
}

public sealed class EfCaptureAiMappingConsentStore(ApiDbContext db, TimeProvider? timeProvider = null) : ICaptureAiMappingConsentStore
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<CaptureAiMappingConsentRecord?> GetAsync(int companyId, CancellationToken cancellationToken) =>
        await db.CaptureAiMappingConsents.IgnoreQueryFilters().AsNoTracking()
            .Where(consent => consent.CompanyId == companyId)
            .Select(consent => new CaptureAiMappingConsentRecord(consent.ConsentVersion, consent.ConsentedAt, consent.ConsentedByUserId, consent.RevokedAt))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    public async Task<bool> GrantAsync(int companyId, string userId, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var version = CaptureAiMappingConsentNotice.CurrentVersion;
        // Replace a revoked or out-of-date consent in one conditional update, so two owners clicking at once record one change.
        var replaced = await db.CaptureAiMappingConsents.IgnoreQueryFilters()
            .Where(consent => consent.CompanyId == companyId && (consent.RevokedAt != null || consent.ConsentVersion != version))
            .ExecuteUpdateAsync(set => set.SetProperty(consent => consent.ConsentVersion, version)
                .SetProperty(consent => consent.ConsentedAt, now)
                .SetProperty(consent => consent.ConsentedByUserId, userId)
                .SetProperty(consent => consent.RevokedAt, (DateTime?)null)
                .SetProperty(consent => consent.RevokedByUserId, (string?)null), cancellationToken).ConfigureAwait(false);
        if (replaced == 1)
            return true;
        if (await db.CaptureAiMappingConsents.IgnoreQueryFilters().AnyAsync(consent => consent.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
            return false; // current consent already exists

        db.CaptureAiMappingConsents.Add(new CaptureAiMappingConsent
        {
            CompanyId = companyId,
            ConsentVersion = version,
            ConsentedAt = now,
            ConsentedByUserId = userId
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException)
        {
            // A concurrent grant won the unique (company) index: the consent exists, so this call changed nothing.
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<bool> RevokeAsync(int companyId, string userId, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        // One conditional update: revoking twice, or racing a grant, is safe.
        var revoked = await db.CaptureAiMappingConsents.IgnoreQueryFilters()
            .Where(consent => consent.CompanyId == companyId && consent.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(consent => consent.RevokedAt, now).SetProperty(consent => consent.RevokedByUserId, userId),
                cancellationToken).ConfigureAwait(false);
        return revoked == 1;
    }
}

/// <summary>
/// Counts the pages a host meters against a monthly cap. The processor reserves before a mapper call and releases when the job does not
/// apply, using the job's own <c>ReservedPages</c> so a retried job never reserves twice. The default meters nothing.
/// </summary>
public interface ICaptureAiMappingMeter
{
    /// <summary>Reserves pages. False when the cap would be exceeded (the job is then skipped and the rule result stands).</summary>
    Task<bool> ReserveAsync(int companyId, int pages, CancellationToken cancellationToken);

    Task ReleaseAsync(int companyId, int pages, CancellationToken cancellationToken);
}

public sealed class NoopCaptureAiMappingMeter : ICaptureAiMappingMeter
{
    public Task<bool> ReserveAsync(int companyId, int pages, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task ReleaseAsync(int companyId, int pages, CancellationToken cancellationToken) => Task.CompletedTask;
}
