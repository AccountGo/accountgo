// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Core.Domain.Security;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Api.PublicIds
{
    /// <summary>
    /// Request-scoped implementation of <see cref="IPublicIdResolver"/>.
    /// Holds only per-request dictionaries; never caches across requests.
    /// </summary>
    public sealed class PublicIdResolver : IPublicIdResolver
    {
        private const int ChunkSize = 1000;

        private readonly ApiDbContext _apiDb;
        private readonly ApplicationIdentityDbContext _identityDb;
        private readonly PublicIdRegistry _registry;
        private readonly ITenantContext _tenant;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IHostEnvironment _environment;
        private readonly ILogger<PublicIdResolver> _logger;

        // Per-request caches only
        private readonly Dictionary<(string Prefix, int Id), string?> _intToPublic = new();
        private readonly Dictionary<(string Prefix, string PublicId), int?> _publicToInt = new();

        private static readonly ConcurrentDictionary<Type, Func<PublicIdResolver, int, string?>> SyncToPublicDelegates = new();
        private static readonly ConcurrentDictionary<Type, Func<PublicIdResolver, IReadOnlyList<int>, CancellationToken, Task<Dictionary<int, string>>>> BatchToPublicDelegates = new();
        private static readonly ConcurrentDictionary<Type, Func<PublicIdResolver, string, int?>> SyncToIntDelegates = new();
        private static readonly ConcurrentDictionary<Type, Func<PublicIdResolver, string, CancellationToken, Task<int?>>> AsyncToIntDelegates = new();

        public PublicIdResolver(
            ApiDbContext apiDb,
            ApplicationIdentityDbContext identityDb,
            PublicIdRegistry registry,
            ITenantContext tenant,
            IHttpContextAccessor httpContextAccessor,
            IHostEnvironment environment,
            ILogger<PublicIdResolver> logger)
        {
            _apiDb = apiDb;
            _identityDb = identityDb;
            _registry = registry;
            _tenant = tenant;
            _httpContextAccessor = httpContextAccessor;
            _environment = environment;
            _logger = logger;
        }

        public string? ToPublic(string prefix, int id)
        {
            if (id <= 0)
                return null;

            if (_intToPublic.TryGetValue((prefix, id), out var cached))
                return cached;

            if (!_registry.TryGetByPrefix(prefix, out var reg))
                throw new InvalidOperationException($"Unknown public ID prefix '{prefix}'.");

            var lookup = SyncToPublicDelegates.GetOrAdd(reg.EntityType, BuildSyncToPublicDelegate);
            var publicId = lookup(this, id);
            // Outbound lookups ignore the workspace filter, so they must not seed the scoped inbound cache (_publicToInt).
            _intToPublic[(prefix, id)] = publicId;
            return publicId;
        }

        public string? ToPublicOrThrow(string prefix, int id)
        {
            if (id <= 0)
                return null;
            var publicId = ToPublic(prefix, id);
            if (string.IsNullOrEmpty(publicId))
            {
                // Loud in development and tests. In production a row can briefly lack an id (an older instance during a
                // rolling deploy), and one such row must not fail a whole response; it is logged for the repair command.
                if (_environment.IsDevelopment() || _environment.IsEnvironment("Testing"))
                    throw new InvalidOperationException($"Public ID lookup failed: no '{prefix}' row found for integer ID {id}.");
                _logger.LogError("No public id for '{Prefix}' row {Id}; emitting null. Run --backfill-public-ids.", prefix, id);
                return null;
            }
            return publicId;
        }

        public async Task PrefetchAsync(IEnumerable<(string Prefix, int Id)> items, CancellationToken cancellationToken = default)
        {
            var grouped = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            foreach (var (prefix, id) in items)
            {
                if (id <= 0 || string.IsNullOrEmpty(prefix))
                    continue;
                if (_intToPublic.ContainsKey((prefix, id)))
                    continue;
                if (!_registry.TryGetByPrefix(prefix, out _))
                    continue;
                if (!grouped.TryGetValue(prefix, out var set))
                {
                    set = new HashSet<int>();
                    grouped[prefix] = set;
                }
                set.Add(id);
            }

            foreach (var (prefix, idSet) in grouped)
            {
                if (!_registry.TryGetByPrefix(prefix, out var reg))
                    continue;

                var batchLookup = BatchToPublicDelegates.GetOrAdd(reg.EntityType, BuildBatchToPublicDelegate);
                var allIds = idSet.ToList();
                for (var offset = 0; offset < allIds.Count; offset += ChunkSize)
                {
                    var chunk = allIds.GetRange(offset, Math.Min(ChunkSize, allIds.Count - offset));
                    var found = await batchLookup(this, chunk, cancellationToken);
                    foreach (var id in chunk)
                    {
                        found.TryGetValue(id, out var pubId);
                        _intToPublic[(prefix, id)] = pubId;
                    }
                }
            }
        }

        public PublicIdLookupResult Resolve(string? publicId, string expectedPrefix)
        {
            if (!PublicIdGenerator.TryParse(publicId, out var actualPrefix, out _))
                return PublicIdLookupResult.Fail(PublicIdLookupError.Malformed, expectedPrefix);

            if (!string.Equals(actualPrefix, expectedPrefix, StringComparison.Ordinal))
                return PublicIdLookupResult.Fail(PublicIdLookupError.WrongKind, expectedPrefix);

            if (!_registry.TryGetByPrefix(expectedPrefix, out var reg))
                throw new InvalidOperationException($"Unknown public ID prefix '{expectedPrefix}'.");

            if (_publicToInt.TryGetValue((expectedPrefix, publicId!), out var cachedId))
                return cachedId.HasValue
                    ? PublicIdLookupResult.Success(cachedId.Value)
                    : PublicIdLookupResult.Fail(PublicIdLookupError.NotFound, expectedPrefix);

            var lookup = SyncToIntDelegates.GetOrAdd(reg.EntityType, BuildSyncToIntDelegate);
            var id = lookup(this, publicId!);
            _publicToInt[(expectedPrefix, publicId!)] = id;
            if (id.HasValue)
                _intToPublic[(expectedPrefix, id.Value)] = publicId;

            return id.HasValue
                ? PublicIdLookupResult.Success(id.Value)
                : PublicIdLookupResult.Fail(PublicIdLookupError.NotFound, expectedPrefix);
        }

        public async Task<PublicIdLookupResult> ResolveAsync(string? publicId, string expectedPrefix, CancellationToken cancellationToken = default)
        {
            if (!PublicIdGenerator.TryParse(publicId, out var actualPrefix, out _))
                return PublicIdLookupResult.Fail(PublicIdLookupError.Malformed, expectedPrefix);

            if (!string.Equals(actualPrefix, expectedPrefix, StringComparison.Ordinal))
                return PublicIdLookupResult.Fail(PublicIdLookupError.WrongKind, expectedPrefix);

            if (!_registry.TryGetByPrefix(expectedPrefix, out var reg))
                throw new InvalidOperationException($"Unknown public ID prefix '{expectedPrefix}'.");

            if (_publicToInt.TryGetValue((expectedPrefix, publicId!), out var cachedId))
                return cachedId.HasValue
                    ? PublicIdLookupResult.Success(cachedId.Value)
                    : PublicIdLookupResult.Fail(PublicIdLookupError.NotFound, expectedPrefix);

            var lookup = AsyncToIntDelegates.GetOrAdd(reg.EntityType, BuildAsyncToIntDelegate);
            var id = await lookup(this, publicId!, cancellationToken);
            _publicToInt[(expectedPrefix, publicId!)] = id;
            if (id.HasValue)
                _intToPublic[(expectedPrefix, id.Value)] = publicId;

            return id.HasValue
                ? PublicIdLookupResult.Success(id.Value)
                : PublicIdLookupResult.Fail(PublicIdLookupError.NotFound, expectedPrefix);
        }

        public string? ToPublicPolymorphic(string? typeName, int id)
        {
            if (id <= 0 || string.IsNullOrWhiteSpace(typeName))
                return null;

            if (_registry.IsExemptTypeName(typeName))
                return null;

            if (!_registry.TryGetPrefixForTypeName(typeName, out var prefix))
            {
                if (_environment.IsDevelopment() || _environment.IsEnvironment("Testing"))
                    throw new InvalidOperationException($"Unmapped polymorphic entity/document type '{typeName}' for PublicId translation.");

                _logger.LogError("Unmapped polymorphic entity/document type '{TypeName}' for PublicId translation; emitting null.", typeName);
                return null;
            }

            return ToPublic(prefix, id);
        }

        public PublicIdLookupResult ResolvePolymorphic(string? publicId, string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName) || !_registry.TryGetPrefixForTypeName(typeName, out var prefix))
                return PublicIdLookupResult.Fail(PublicIdLookupError.Malformed);

            return Resolve(publicId, prefix);
        }

        private string? GetCurrentUserId() =>
            _tenant.UserId
            ?? _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? _httpContextAccessor.HttpContext?.User?.FindFirst("sub")?.Value;

        private string? GetCurrentUserEmail() =>
            _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Email)?.Value
            ?? _httpContextAccessor.HttpContext?.User?.FindFirst("email")?.Value;

        private IQueryable<TEntity> ScopedQuery<TEntity>(bool forReadOutbound)
            where TEntity : class, IHasPublicId
        {
            var reg = _registry.ByType[typeof(TEntity)];
            if (reg.IsIdentityEntity)
            {
                var set = _identityDb.Set<TEntity>().AsNoTracking();
                if (forReadOutbound)
                    return set;

                var userId = GetCurrentUserId();
                if (string.IsNullOrEmpty(userId))
                    return set.Where(_ => false);

                return FilterByStringProperty(set, "UserId", userId);
            }

            var apiSet = _apiDb.Set<TEntity>().AsNoTracking();
            if (forReadOutbound)
            {
                // Outbound translation of an ID already produced by an authorized controller/service:
                // ignore query filters so cross-company DTOs (e.g. linked personal/work workspaces, pending invitations) translate cleanly.
                return apiSet.IgnoreQueryFilters();
            }

            switch (reg.ScopeRule)
            {
                case PublicIdScopeRule.TenantOwned:
                    // EF global tenant query filter already restricts CompanyId == CurrentTenantCompanyId.
                    return apiSet;

                case PublicIdScopeRule.CompanyMembership:
                {
                    var userId = GetCurrentUserId();
                    var activeCompanyId = _tenant.CompanyId;
                    var companies = (IQueryable<Company>)apiSet;
                    if (string.IsNullOrEmpty(userId))
                    {
                        return activeCompanyId.HasValue
                            ? (IQueryable<TEntity>)companies.Where(c => c.Id == activeCompanyId.Value)
                            : apiSet.Where(_ => false);
                    }
                    var memberCompanyIds = _apiDb.CompanyMembers.AsNoTracking()
                        .Where(m => m.UserId == userId && m.Status == CompanyMemberStatus.Active)
                        .Select(m => m.CompanyId);
                    return (IQueryable<TEntity>)companies.Where(c =>
                        memberCompanyIds.Contains(c.Id)
                        || (activeCompanyId.HasValue && c.Id == activeCompanyId.Value));
                }

                case PublicIdScopeRule.CompanyOrSystemRole:
                    // SecurityRole templates are global across workspaces.
                    return apiSet;

                case PublicIdScopeRule.CompanyScopedDirect:
                {
                    if (typeof(TEntity) == typeof(CompanyMember))
                    {
                        var companyId = _tenant.CompanyId;
                        if (!companyId.HasValue) return apiSet.Where(_ => false);
                        return (IQueryable<TEntity>)((IQueryable<CompanyMember>)apiSet).Where(m => m.CompanyId == companyId.Value);
                    }
                    if (typeof(TEntity) == typeof(CompanyInvitation))
                    {
                        var companyId = _tenant.CompanyId;
                        var email = GetCurrentUserEmail()?.Trim().ToLowerInvariant();
                        return (IQueryable<TEntity>)((IQueryable<CompanyInvitation>)apiSet).Where(i =>
                            (companyId.HasValue && i.CompanyId == companyId.Value)
                            || (email != null && i.Email.ToLower() == email));
                    }
                    if (typeof(TEntity) == typeof(LinkedEntryPair))
                    {
                        var companyId = _tenant.CompanyId;
                        if (!companyId.HasValue) return apiSet.Where(_ => false);
                        return (IQueryable<TEntity>)((IQueryable<LinkedEntryPair>)apiSet)
                            .Where(p => p.SourceCompanyId == companyId.Value || p.MirrorCompanyId == companyId.Value);
                    }
                    return apiSet.Where(_ => false);
                }

                default:
                    return apiSet.Where(_ => false);
            }
        }

        private static IQueryable<TEntity> FilterByStringProperty<TEntity>(IQueryable<TEntity> query, string propertyName, string value)
        {
            var param = Expression.Parameter(typeof(TEntity), "e");
            var prop = Expression.Property(param, propertyName);
            var body = Expression.Equal(prop, Expression.Constant(value));
            var lambda = Expression.Lambda<Func<TEntity, bool>>(body, param);
            return query.Where(lambda);
        }

        private static Expression<Func<TEntity, int>> IdSelector<TEntity>()
        {
            var param = Expression.Parameter(typeof(TEntity), "e");
            return Expression.Lambda<Func<TEntity, int>>(Expression.Property(param, "Id"), param);
        }

        private static Expression<Func<TEntity, bool>> IdEquals<TEntity>(int id)
        {
            var param = Expression.Parameter(typeof(TEntity), "e");
            return Expression.Lambda<Func<TEntity, bool>>(
                Expression.Equal(Expression.Property(param, "Id"), Expression.Constant(id)),
                param);
        }

        private static Expression<Func<TEntity, bool>> IdContains<TEntity>(IReadOnlyList<int> ids)
        {
            var param = Expression.Parameter(typeof(TEntity), "e");
            var contains = typeof(Enumerable).GetMethods()
                .First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
                .MakeGenericMethod(typeof(int));
            var body = Expression.Call(contains, Expression.Constant(ids), Expression.Property(param, "Id"));
            return Expression.Lambda<Func<TEntity, bool>>(body, param);
        }

        private string? SyncToPublicCore<TEntity>(int id) where TEntity : class, IHasPublicId =>
            ScopedQuery<TEntity>(forReadOutbound: true)
                .Where(IdEquals<TEntity>(id))
                .Select(e => e.PublicId)
                .FirstOrDefault();

        private async Task<Dictionary<int, string>> BatchToPublicCore<TEntity>(IReadOnlyList<int> ids, CancellationToken ct)
            where TEntity : class, IHasPublicId
        {
            var param = Expression.Parameter(typeof(TEntity), "e");
            var idProp = Expression.Property(param, "Id");
            var pubProp = Expression.Property(param, nameof(IHasPublicId.PublicId));
            var ctor = typeof(IdPair).GetConstructor(new[] { typeof(int), typeof(string) })!;
            var selector = Expression.Lambda<Func<TEntity, IdPair>>(
                Expression.New(ctor, idProp, pubProp), param);

            var rows = await ScopedQuery<TEntity>(forReadOutbound: true)
                .Where(IdContains<TEntity>(ids))
                .Select(selector)
                .ToListAsync(ct);

            var result = new Dictionary<int, string>(rows.Count);
            foreach (var row in rows)
                result[row.Id] = row.PublicId;
            return result;
        }

        private int? SyncToIntCore<TEntity>(string publicId) where TEntity : class, IHasPublicId
        {
            var idSelector = IdSelector<TEntity>();
            var matches = ScopedQuery<TEntity>(forReadOutbound: false)
                .Where(e => e.PublicId == publicId)
                .Select(idSelector)
                .Take(1)
                .ToList();
            return matches.Count > 0 ? matches[0] : null;
        }

        private async Task<int?> AsyncToIntCore<TEntity>(string publicId, CancellationToken ct) where TEntity : class, IHasPublicId
        {
            var idSelector = IdSelector<TEntity>();
            var matches = await ScopedQuery<TEntity>(forReadOutbound: false)
                .Where(e => e.PublicId == publicId)
                .Select(idSelector)
                .Take(1)
                .ToListAsync(ct);
            return matches.Count > 0 ? matches[0] : null;
        }

        private sealed class IdPair
        {
            public int Id { get; }
            public string PublicId { get; }
            public IdPair(int id, string publicId)
            {
                Id = id;
                PublicId = publicId;
            }
        }

        private static Func<PublicIdResolver, int, string?> BuildSyncToPublicDelegate(Type entityType)
        {
            var method = typeof(PublicIdResolver).GetMethod(nameof(SyncToPublicCore), BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(entityType);
            return (Func<PublicIdResolver, int, string?>)method.CreateDelegate(typeof(Func<PublicIdResolver, int, string?>));
        }

        private static Func<PublicIdResolver, IReadOnlyList<int>, CancellationToken, Task<Dictionary<int, string>>> BuildBatchToPublicDelegate(Type entityType)
        {
            var method = typeof(PublicIdResolver).GetMethod(nameof(BatchToPublicCore), BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(entityType);
            return (Func<PublicIdResolver, IReadOnlyList<int>, CancellationToken, Task<Dictionary<int, string>>>)
                method.CreateDelegate(typeof(Func<PublicIdResolver, IReadOnlyList<int>, CancellationToken, Task<Dictionary<int, string>>>));
        }

        private static Func<PublicIdResolver, string, int?> BuildSyncToIntDelegate(Type entityType)
        {
            var method = typeof(PublicIdResolver).GetMethod(nameof(SyncToIntCore), BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(entityType);
            return (Func<PublicIdResolver, string, int?>)method.CreateDelegate(typeof(Func<PublicIdResolver, string, int?>));
        }

        private static Func<PublicIdResolver, string, CancellationToken, Task<int?>> BuildAsyncToIntDelegate(Type entityType)
        {
            var method = typeof(PublicIdResolver).GetMethod(nameof(AsyncToIntCore), BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(entityType);
            return (Func<PublicIdResolver, string, CancellationToken, Task<int?>>)method.CreateDelegate(typeof(Func<PublicIdResolver, string, CancellationToken, Task<int?>>));
        }
    }
}
