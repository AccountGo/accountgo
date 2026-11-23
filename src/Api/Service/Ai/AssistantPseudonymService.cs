// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Api.Service.Ai;

public sealed class AssistantPseudonymKeyProvider : IAssistantPseudonymKeyProvider
{
    private const string KeyContext = "assistant-pseudonym-v1";

    public byte[] AppDerivedKey { get; }

    public AssistantPseudonymKeyProvider(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var secretKey = configuration["SECRET_KEY"]
            ?? configuration["SECRET:key"]
            ?? configuration["SECRET__key"]
            ?? Environment.GetEnvironmentVariable("SECRET_KEY");

        if (string.IsNullOrWhiteSpace(secretKey) || Encoding.UTF8.GetByteCount(secretKey) < 32)
        {
            throw new InvalidOperationException(
                "SECRET_KEY must be configured with at least 32 bytes to derive assistant pseudonym keys.");
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        AppDerivedKey = hmac.ComputeHash(Encoding.UTF8.GetBytes(KeyContext));
    }
}

public sealed partial class AssistantPseudonymService : IAssistantPseudonymService
{
    private static readonly Regex TokenPattern = GenerateTokenRegex();

    private readonly ApiDbContext _db;
    private readonly IAssistantPseudonymKeyProvider _keyProvider;
    private readonly TimeProvider _timeProvider;
    private byte[]? _ephemeralKey;

    // Request-scoped caches to avoid duplicate DB lookups/writes within one MCP tool call.
    private readonly Dictionary<(int CompanyId, string UserId, string SessionKey, string Kind, string PublicId), string> _entityToToken = new();
    private readonly Dictionary<(int CompanyId, string UserId, string SessionKey, string Token), AssistantPseudonymEntity> _tokenToEntity = new();

    public AssistantPseudonymService(
        ApiDbContext db,
        IAssistantPseudonymKeyProvider keyProvider,
        TimeProvider? timeProvider = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public static bool IsPseudonymToken(string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate) && TokenPattern.IsMatch(candidate.Trim());

    public async Task<string> PseudonymizeAsync(
        int companyId,
        string userId,
        string? sessionId,
        string kind,
        string publicId,
        CancellationToken cancellationToken = default)
    {
        var entity = NormalizeEntity(kind, publicId);
        var map = await PseudonymizeBatchAsync(companyId, userId, sessionId, new[] { entity }, cancellationToken)
            .ConfigureAwait(false);
        return map[entity];
    }

    public async Task<IReadOnlyDictionary<AssistantPseudonymEntity, string>> PseudonymizeBatchAsync(
        int companyId,
        string userId,
        string? sessionId,
        IEnumerable<AssistantPseudonymEntity> entities,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new ArgumentOutOfRangeException(nameof(companyId), "Company ID must be positive.");
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID is required.", nameof(userId));
        ArgumentNullException.ThrowIfNull(entities);

        var normalizedUserId = userId.Trim();
        var normalizedSessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim();
        var distinct = new List<AssistantPseudonymEntity>();
        var seen = new HashSet<AssistantPseudonymEntity>();
        foreach (var raw in entities)
        {
            var normalized = NormalizeEntity(raw.Kind, raw.PublicId);
            if (seen.Add(normalized))
            {
                distinct.Add(normalized);
            }
        }

        var result = new Dictionary<AssistantPseudonymEntity, string>(distinct.Count);
        if (distinct.Count == 0)
        {
            return result;
        }

        if (normalizedSessionId is null)
        {
            // Ephemeral per-request key: never fall back to clear values and never persist.
            var ephemeralKey = GetOrCreateEphemeralKey();
            const string ephemeralScope = "__ephemeral__";
            foreach (var entity in distinct)
            {
                result[entity] = ComputeEphemeralToken(companyId, normalizedUserId, ephemeralScope, ephemeralKey, entity);
            }
            return result;
        }

        var sessionKey = DeriveSessionKey(companyId, normalizedUserId, normalizedSessionId);
        var uncached = new List<(AssistantPseudonymEntity Entity, string Candidate8, string Candidate12)>();

        foreach (var entity in distinct)
        {
            var cacheKey = (companyId, normalizedUserId, normalizedSessionId, entity.Kind, entity.PublicId);
            if (_entityToToken.TryGetValue(cacheKey, out var cachedToken))
            {
                result[entity] = cachedToken;
                continue;
            }

            var (c8, c12) = ComputeCandidateTokens(sessionKey, entity.Kind, entity.PublicId);
            uncached.Add((entity, c8, c12));
        }

        if (uncached.Count == 0)
        {
            return result;
        }

        var candidateTokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in uncached)
        {
            candidateTokens.Add(item.Candidate8);
            candidateTokens.Add(item.Candidate12);
        }

        var existingRows = await _db.AssistantPseudonyms
            .Where(row => row.CompanyId == companyId
                && row.UserId == normalizedUserId
                && row.SessionId == normalizedSessionId
                && candidateTokens.Contains(row.Token))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingByToken = existingRows.ToDictionary(row => row.Token, StringComparer.Ordinal);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = now.Add(AssistantPseudonymRetentionService.DefaultTtl);
        var hasDbChanges = false;

        foreach (var (entity, c8, c12) in uncached)
        {
            string chosenToken;
            if (existingByToken.TryGetValue(c8, out var row8))
            {
                if (string.Equals(row8.Kind, entity.Kind, StringComparison.Ordinal)
                    && string.Equals(row8.PublicId, entity.PublicId, StringComparison.Ordinal))
                {
                    row8.LastUsedAt = now;
                    row8.ExpiresAt = expiresAt;
                    hasDbChanges = true;
                    chosenToken = c8;
                }
                else
                {
                    // 8-hex collision within this session: extend to 12 uppercase hex.
                    chosenToken = c12;
                    if (existingByToken.TryGetValue(c12, out var row12))
                    {
                        row12.LastUsedAt = now;
                        row12.ExpiresAt = expiresAt;
                        hasDbChanges = true;
                    }
                    else
                    {
                        var inserted = CreateRow(companyId, normalizedUserId, normalizedSessionId, c12, entity, now, expiresAt);
                        _db.AssistantPseudonyms.Add(inserted);
                        existingByToken[c12] = inserted;
                        hasDbChanges = true;
                    }
                }
            }
            else
            {
                var inserted = CreateRow(companyId, normalizedUserId, normalizedSessionId, c8, entity, now, expiresAt);
                _db.AssistantPseudonyms.Add(inserted);
                existingByToken[c8] = inserted;
                hasDbChanges = true;
                chosenToken = c8;
            }

            result[entity] = chosenToken;
            _entityToToken[(companyId, normalizedUserId, normalizedSessionId, entity.Kind, entity.PublicId)] = chosenToken;
            _tokenToEntity[(companyId, normalizedUserId, normalizedSessionId, chosenToken)] = entity;
        }

        if (hasDbChanges)
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                // Concurrent insert race for the same (CompanyId, UserId, SessionId, Token).
                // Detach newly added pseudonym rows and fall back to per-entity resolution.
                foreach (var entry in _db.ChangeTracker.Entries<AssistantPseudonym>().Where(e => e.State == EntityState.Added).ToList())
                {
                    entry.State = EntityState.Detached;
                }

                var refreshed = await _db.AssistantPseudonyms.AsNoTracking()
                    .Where(row => row.CompanyId == companyId
                        && row.UserId == normalizedUserId
                        && row.SessionId == normalizedSessionId
                        && candidateTokens.Contains(row.Token))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var (entity, c8, c12) in uncached)
                {
                    var match = refreshed.FirstOrDefault(r =>
                        string.Equals(r.Kind, entity.Kind, StringComparison.Ordinal)
                        && string.Equals(r.PublicId, entity.PublicId, StringComparison.Ordinal));
                    result[entity] = match?.Token ?? c12;
                }
            }
        }

        return result;
    }

    public async Task<AssistantPseudonymResolution?> ResolveTokenAsync(
        int companyId,
        string userId,
        string? sessionId,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0 || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        if (!IsPseudonymToken(token))
        {
            return null;
        }

        var normalizedToken = token.Trim();
        var map = await ResolveTokensAsync(companyId, userId, sessionId, new[] { normalizedToken }, cancellationToken)
            .ConfigureAwait(false);
        return map.TryGetValue(normalizedToken, out var resolved) ? resolved : null;
    }

    public async Task<IReadOnlyDictionary<string, AssistantPseudonymResolution>> ResolveTokensAsync(
        int companyId,
        string userId,
        string sessionId,
        IEnumerable<string> tokens,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, AssistantPseudonymResolution>(StringComparer.Ordinal);
        if (companyId <= 0 || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionId) || tokens is null)
        {
            return result;
        }

        var validTokens = tokens
            .Where(IsPseudonymToken)
            .Select(t => t.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (validTokens.Count == 0)
        {
            return result;
        }

        var normalizedUserId = userId.Trim();
        var normalizedSessionId = sessionId.Trim();
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var rows = await _db.AssistantPseudonyms.AsNoTracking()
            .Where(row => row.CompanyId == companyId
                && row.UserId == normalizedUserId
                && row.SessionId == normalizedSessionId
                && row.ExpiresAt > now
                && validTokens.Contains(row.Token))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in rows)
        {
            result[row.Token] = new AssistantPseudonymResolution(row.Token, row.Kind, row.PublicId);
        }

        return result;
    }

    private byte[] DeriveSessionKey(int companyId, string userId, string sessionId)
    {
        using var hmac = new HMACSHA256(_keyProvider.AppDerivedKey);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes($"{companyId}|{userId}|{sessionId}"));
    }

    private byte[] GetOrCreateEphemeralKey()
    {
        _ephemeralKey ??= RandomNumberGenerator.GetBytes(32);
        return _ephemeralKey;
    }

    private string ComputeEphemeralToken(
        int companyId,
        string userId,
        string scope,
        byte[] ephemeralKey,
        AssistantPseudonymEntity entity)
    {
        var entityKey = (companyId, userId, scope, entity.Kind, entity.PublicId);
        if (_entityToToken.TryGetValue(entityKey, out var cached))
        {
            return cached;
        }

        var (c8, c12) = ComputeCandidateTokens(ephemeralKey, entity.Kind, entity.PublicId);
        var tokenKey8 = (companyId, userId, scope, c8);
        var chosen = _tokenToEntity.TryGetValue(tokenKey8, out var existing) && existing != entity
            ? c12
            : c8;

        _entityToToken[entityKey] = chosen;
        _tokenToEntity[(companyId, userId, scope, chosen)] = entity;
        return chosen;
    }

    internal static (string Token8, string Token12) ComputeCandidateTokens(byte[] sessionKey, string kind, string publicId)
    {
        var prefix = AssistantPseudonymKind.RolePrefix(kind);
        using var hmac = new HMACSHA256(sessionKey);
        var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{kind}|{publicId}"));
        var hex8 = Convert.ToHexString(digest.AsSpan(0, 4));
        var hex12 = Convert.ToHexString(digest.AsSpan(0, 6));
        return ($"{prefix}-{hex8}", $"{prefix}-{hex12}");
    }

    private static AssistantPseudonymEntity NormalizeEntity(string kind, string publicId)
    {
        if (string.IsNullOrWhiteSpace(kind))
            throw new ArgumentException("Entity kind is required.", nameof(kind));
        if (string.IsNullOrWhiteSpace(publicId))
            throw new ArgumentException("Entity public ID is required.", nameof(publicId));

        var normalizedKind = kind.Trim();
        _ = AssistantPseudonymKind.RolePrefix(normalizedKind);
        return new AssistantPseudonymEntity(normalizedKind, publicId.Trim());
    }

    private static AssistantPseudonym CreateRow(
        int companyId,
        string userId,
        string sessionId,
        string token,
        AssistantPseudonymEntity entity,
        DateTime now,
        DateTime expiresAt) => new()
    {
        CompanyId = companyId,
        UserId = userId,
        SessionId = sessionId,
        Token = token,
        Kind = entity.Kind,
        PublicId = entity.PublicId,
        CreatedAt = now,
        LastUsedAt = now,
        ExpiresAt = expiresAt,
    };

    [GeneratedRegex(@"^[CVPB]-[0-9A-F]{8}([0-9A-F]{4})?$", RegexOptions.CultureInvariant)]
    private static partial Regex GenerateTokenRegex();
}
