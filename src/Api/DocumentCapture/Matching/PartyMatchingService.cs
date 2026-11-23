// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Api.Data;
using Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Api.DocumentCaptures.Matching;

public sealed record PartyMatchCandidate(
    string PartyId,
    string Name,
    string Reason,
    double Score,
    bool Archived);

/// <summary>
/// Server-side tenant-scoped party matcher for document captures (rules M-01..M-06).
/// Never returns the full party list, never compares masked values ("••••"), and never uses IgnoreQueryFilters.
/// </summary>
public static class PartyMatchingService
{
    public const string RoleVendor = "vendor";
    public const string RoleCustomer = "customer";

    public const string ReasonIdentifier = "identifier";
    public const string ReasonAlias = "alias";
    public const string ReasonName = "name";

    private const string MaskMarker = "••••";
    private const int MinPhoneDigits = 8;

    private const RegexOptions LinearI = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;

    private static readonly Regex EmailRegex = new(
        @"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b",
        LinearI);

    private static readonly Regex PhoneLineRegex = new(
        @"\b(?:tel|telephone|phone|fax|mobile|cell|call|hotline|โทร)\b",
        LinearI);

    private static readonly Regex PhoneTokenRegex = new(
        @"(?:\+?\d[\d\s().\-]{6,22}\d)",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private sealed record CandidateSource(
        string PublicId,
        int PartyId,
        string Name,
        string? Email,
        string? Phone,
        string? Fax,
        bool Archived,
        DateTime? LastUsedAt);

    public static async Task<IReadOnlyList<PartyMatchCandidate>?> MatchCaptureAsync(
        ApiDbContext db,
        string role,
        int captureId,
        CancellationToken cancellationToken)
    {
        var normalizedRole = NormalizeRole(role);
        if (normalizedRole is null)
            throw new ArgumentException("Role must be 'vendor' or 'customer'.", nameof(role));

        var capture = await db.DocumentCaptures
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == captureId, cancellationToken)
            .ConfigureAwait(false);
        if (capture is null)
            return null;

        var extractedName = ExtractPartyName(capture, normalizedRole);
        var (emails, phoneSuffixes) = ExtractIdentifiers(capture);
        var normalizedName = PartyNameNormalizer.Normalize(extractedName);

        if (string.IsNullOrEmpty(normalizedName) && emails.Count == 0 && phoneSuffixes.Count == 0)
            return Array.Empty<PartyMatchCandidate>();

        var sources = await LoadCandidateSourcesAsync(db, normalizedRole, cancellationToken).ConfigureAwait(false);
        if (sources.Count == 0)
            return Array.Empty<PartyMatchCandidate>();

        HashSet<int> aliasPartyIds = [];
        if (!string.IsNullOrEmpty(normalizedName))
        {
            var matchedAliasIds = await db.PartyAliases
                .AsNoTracking()
                .Where(a => a.Role == normalizedRole && a.NormalizedAlias == normalizedName)
                .Select(a => a.PartyId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            aliasPartyIds = matchedAliasIds.ToHashSet();
        }

        return RankCandidates(sources, normalizedName, emails, phoneSuffixes, aliasPartyIds);
    }

    /// <summary>
    /// Records the normalized extracted name from one or more captures as an alias of the chosen party
    /// whenever the matcher did not already propose that party via alias or identifier (rule M-04).
    /// Must run inside the same tenant-scoped transaction as the bill or invoice save.
    /// </summary>
    public static void RecordAliasesForCaptures(
        ApiDbContext db,
        int companyId,
        string role,
        int chosenPartyId,
        IEnumerable<int> captureIds,
        string? actor)
    {
        var normalizedRole = NormalizeRole(role);
        if (normalizedRole is null || chosenPartyId <= 0)
            return;

        var distinctCaptureIds = captureIds.Where(id => id > 0).Distinct().ToList();
        if (distinctCaptureIds.Count == 0)
            return;

        var captures = db.DocumentCaptures
            .AsNoTracking()
            .Where(c => distinctCaptureIds.Contains(c.Id))
            .ToList();
        if (captures.Count == 0)
            return;

        var chosenParty = db.Parties
            .AsNoTracking()
            .FirstOrDefault(p => p.Id == chosenPartyId);
        if (chosenParty is null)
            return;

        var now = DateTime.UtcNow;
        var createdBy = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim();
        if (createdBy.Length > 256)
            createdBy = createdBy[..256];

        var seenAliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var capture in captures)
        {
            var extractedName = ExtractPartyName(capture, normalizedRole, preferRawExtracted: true);
            var normalizedAlias = PartyNameNormalizer.Normalize(extractedName);
            if (string.IsNullOrEmpty(normalizedAlias) || !seenAliases.Add(normalizedAlias))
                continue;

            var (emails, phoneSuffixes) = ExtractIdentifiers(capture);
            if (MatchesIdentifier(chosenParty.Email, chosenParty.Phone, chosenParty.Fax, emails, phoneSuffixes))
                continue;

            var existing = db.PartyAliases
                .FirstOrDefault(a => a.Role == normalizedRole && a.NormalizedAlias == normalizedAlias);
            if (existing is not null)
            {
                if (existing.PartyId == chosenPartyId)
                    continue;
                existing.PartyId = chosenPartyId;
                existing.CreatedBy = createdBy;
                existing.CreatedAt = now;
            }
            else
            {
                db.PartyAliases.Add(new PartyAlias
                {
                    CompanyId = companyId,
                    PartyId = chosenPartyId,
                    Role = normalizedRole,
                    NormalizedAlias = normalizedAlias,
                    CreatedBy = createdBy,
                    CreatedAt = now,
                });
            }
        }
    }

    public static string? NormalizeRole(string? role) => role?.Trim().ToLowerInvariant() switch
    {
        RoleVendor => RoleVendor,
        RoleCustomer => RoleCustomer,
        _ => null,
    };

    internal static (HashSet<string> Emails, HashSet<string> PhoneSuffixes) ExtractIdentifiers(DocumentCapture capture)
    {
        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var phoneSuffixes = new HashSet<string>(StringComparer.Ordinal);

        CollectIdentifiersFromText(capture.RawText, emails, phoneSuffixes);
        CollectIdentifiersFromJson(capture.AcceptedFieldsJson ?? capture.FieldsJson, emails, phoneSuffixes);

        return (emails, phoneSuffixes);
    }

    internal static void CollectIdentifiersFromText(string? text, HashSet<string> emails, HashSet<string> phoneSuffixes)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            foreach (Match m in EmailRegex.Matches(line))
            {
                var candidate = m.Value.Trim();
                if (!ContainsMask(candidate))
                    emails.Add(candidate);
            }

            if (PhoneLineRegex.IsMatch(line))
            {
                foreach (Match m in PhoneTokenRegex.Matches(line))
                {
                    if (ContainsMask(m.Value))
                        continue;
                    var suffix = ExtractPhoneLast8(m.Value);
                    if (suffix is not null)
                        phoneSuffixes.Add(suffix);
                }
            }
        }
    }

    private static void CollectIdentifiersFromJson(string? json, HashSet<string> emails, HashSet<string> phoneSuffixes)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return;

            if (doc.RootElement.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.String)
                CollectIdentifiersFromText(notes.GetString(), emails, phoneSuffixes);
        }
        catch (JsonException)
        {
            // Ignore malformed JSON; schema validation handles capture integrity separately.
        }
    }

    internal static string? ExtractPartyName(DocumentCapture capture, string role, bool preferRawExtracted = false)
    {
        var propertyName = role == RoleVendor ? "vendorName" : "customerName";
        var primaryJson = preferRawExtracted
            ? (capture.FieldsJson ?? capture.AcceptedFieldsJson)
            : (capture.AcceptedFieldsJson ?? capture.FieldsJson);
        var secondaryJson = preferRawExtracted ? capture.AcceptedFieldsJson : capture.FieldsJson;

        return ReadJsonString(primaryJson, propertyName) ?? ReadJsonString(secondaryJson, propertyName);
    }

    private static string? ReadJsonString(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(propertyName, out var prop)
                && prop.ValueKind == JsonValueKind.String)
            {
                var value = prop.GetString()?.Trim();
                return string.IsNullOrEmpty(value) || ContainsMask(value) ? null : value;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    internal static bool MatchesIdentifier(
        string? partyEmail,
        string? partyPhone,
        string? partyFax,
        HashSet<string> extractedEmails,
        HashSet<string> extractedPhoneSuffixes)
    {
        if (extractedEmails.Count > 0 && !string.IsNullOrWhiteSpace(partyEmail))
        {
            var cleanEmail = partyEmail.Trim();
            if (!ContainsMask(cleanEmail) && extractedEmails.Contains(cleanEmail))
                return true;
        }

        if (extractedPhoneSuffixes.Count > 0)
        {
            var phoneSuffix = ExtractPhoneLast8(partyPhone);
            if (phoneSuffix is not null && extractedPhoneSuffixes.Contains(phoneSuffix))
                return true;

            var faxSuffix = ExtractPhoneLast8(partyFax);
            if (faxSuffix is not null && extractedPhoneSuffixes.Contains(faxSuffix))
                return true;
        }

        return false;
    }

    internal static string? ExtractPhoneLast8(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || ContainsMask(raw))
            return null;

        var digits = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (ch is >= '0' and <= '9')
                digits.Append(ch);
        }

        if (digits.Length < MinPhoneDigits)
            return null;

        var s = digits.ToString();
        return s[^MinPhoneDigits..];
    }

    internal static bool ContainsMask(string value) =>
        value.Contains(MaskMarker, StringComparison.Ordinal);

    private static async Task<List<CandidateSource>> LoadCandidateSourcesAsync(
        ApiDbContext db,
        string role,
        CancellationToken cancellationToken)
    {
        if (role == RoleVendor)
        {
            var lastBillByVendor = await db.PurchaseInvoiceHeaders
                .AsNoTracking()
                .Where(b => b.VendorId != null)
                .GroupBy(b => b.VendorId!.Value)
                .Select(g => new { VendorId = g.Key, LastDate = g.Max(b => b.Date) })
                .ToDictionaryAsync(x => x.VendorId, x => (DateTime?)x.LastDate, cancellationToken)
                .ConfigureAwait(false);

            var vendors = await db.Vendors
                .AsNoTracking()
                .Include(v => v.Party)
                .Where(v => v.Party != null && v.PartyId != null)
                .Select(v => new
                {
                    v.Id,
                    v.PublicId,
                    PartyId = v.PartyId!.Value,
                    Name = v.Party!.Name ?? string.Empty,
                    v.Party.Email,
                    v.Party.Phone,
                    v.Party.Fax,
                    Archived = v.ArchivedAt != null,
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return vendors.Select(v => new CandidateSource(
                v.PublicId,
                v.PartyId,
                v.Name,
                v.Email,
                v.Phone,
                v.Fax,
                v.Archived,
                lastBillByVendor.GetValueOrDefault(v.Id))).ToList();
        }

        var lastInvoiceByCustomer = await db.SalesInvoiceHeaders
            .AsNoTracking()
            .GroupBy(i => i.CustomerId)
            .Select(g => new { CustomerId = g.Key, LastDate = g.Max(i => i.Date) })
            .ToDictionaryAsync(x => x.CustomerId, x => (DateTime?)x.LastDate, cancellationToken)
            .ConfigureAwait(false);

        var customers = await db.Customers
            .AsNoTracking()
            .Include(c => c.Party)
            .Where(c => c.Party != null && c.PartyId != null && !c.IsSystem)
            .Select(c => new
            {
                c.Id,
                c.PublicId,
                PartyId = c.PartyId!.Value,
                Name = c.Party!.Name ?? string.Empty,
                c.Party.Email,
                c.Party.Phone,
                c.Party.Fax,
                Archived = c.ArchivedAt != null,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return customers.Select(c => new CandidateSource(
            c.PublicId,
            c.PartyId,
            c.Name,
            c.Email,
            c.Phone,
            c.Fax,
            c.Archived,
            lastInvoiceByCustomer.GetValueOrDefault(c.Id))).ToList();
    }

    private static IReadOnlyList<PartyMatchCandidate> RankCandidates(
        List<CandidateSource> sources,
        string normalizedExtractedName,
        HashSet<string> extractedEmails,
        HashSet<string> extractedPhoneSuffixes,
        HashSet<int> aliasPartyIds)
    {
        var scored = new List<(PartyMatchCandidate Candidate, int Tier, DateTime? LastUsedAt)>();

        foreach (var s in sources)
        {
            if (MatchesIdentifier(s.Email, s.Phone, s.Fax, extractedEmails, extractedPhoneSuffixes))
            {
                scored.Add((new PartyMatchCandidate(s.PublicId, s.Name, ReasonIdentifier, 1.0, s.Archived), 0, s.LastUsedAt));
                continue;
            }

            if (aliasPartyIds.Contains(s.PartyId))
            {
                scored.Add((new PartyMatchCandidate(s.PublicId, s.Name, ReasonAlias, 1.0, s.Archived), 1, s.LastUsedAt));
                continue;
            }

            if (!string.IsNullOrEmpty(normalizedExtractedName))
            {
                var normalizedParty = PartyNameNormalizer.Normalize(s.Name);
                var score = PartyNameMatcher.ScoreNormalized(normalizedExtractedName, normalizedParty);
                if (score >= PartyNameMatcher.MatchThreshold)
                {
                    scored.Add((new PartyMatchCandidate(s.PublicId, s.Name, ReasonName, score, s.Archived), 2, s.LastUsedAt));
                }
            }
        }

        return scored
            .OrderBy(x => x.Tier)
            .ThenByDescending(x => x.Candidate.Score)
            .ThenByDescending(x => x.LastUsedAt ?? DateTime.MinValue)
            .ThenBy(x => x.Candidate.Name, StringComparer.OrdinalIgnoreCase)
            .Take(PartyNameMatcher.MaxCandidates)
            .Select(x => x.Candidate)
            .ToList();
    }
}
