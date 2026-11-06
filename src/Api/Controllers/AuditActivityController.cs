// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Api.Data;
using Api.Filters;
using Core.Domain.Auditing;
using Core.Domain.Security;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace Api.Controllers
{
    [Authorize]
    [TenantScoped]
    [ApiController]
    [Route("api/organizations/current")]
    public sealed class AuditActivityController : BaseController
    {
        private const int ExportRowCap = 10_000;

        private readonly ApiDbContext _db;
        private readonly ApplicationIdentityDbContext _identity;
        private readonly ITenantContext _tenant;
        private readonly IEntitlementService _entitlements;

        public AuditActivityController(
            ApiDbContext db,
            ApplicationIdentityDbContext identity,
            ITenantContext tenant,
            IEntitlementService entitlements)
        {
            _db = db;
            _identity = identity;
            _tenant = tenant;
            _entitlements = entitlements;
        }

        [RequirePermission(SystemPermissions.AuditLogsView)]
        [HttpGet("activity")]
        public async Task<IActionResult> Activity(
            [FromQuery] string? entityType = null,
            [FromQuery] int? entityId = null,
            [FromQuery] bool includeChildren = false,
            [FromQuery] string? actorUserId = null,
            [FromQuery] DateTimeOffset? from = null,
            [FromQuery] DateTimeOffset? to = null,
            [FromQuery] long? cursor = null,
            [FromQuery] int limit = 50)
        {
            var companyId = _tenant.RequireCompanyId();
            limit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 100);
            var singleRecord = !string.IsNullOrWhiteSpace(entityType) && entityId.HasValue;
            var access = AuditHistoryAccess.Evaluate(_entitlements, companyId);
            int? windowDays = null;

            if (!singleRecord)
            {
                if (access == AuditHistoryAccessLevel.SingleRecordOnly)
                    return FeatureNotEntitled(FeatureKeys.AuditActivity,
                        "Activity list is not available for this workspace.");
                if (access == AuditHistoryAccessLevel.Recent90Days)
                {
                    var floor = DateTime.UtcNow.AddDays(-AuditHistoryAccess.RecentWindowDays);
                    var fromUtc = from?.UtcDateTime ?? floor;
                    if (fromUtc < floor) fromUtc = floor;
                    from = new DateTimeOffset(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc));
                    windowDays = AuditHistoryAccess.RecentWindowDays;
                }
            }

            var query = BuildActivityQuery(companyId, entityType, entityId, includeChildren, actorUserId, from, to, cursor);

            var rows = await query
                .OrderByDescending(r => r.Id)
                .Take(limit + 1)
                .ToListAsync();

            var page = rows.Take(limit).ToList();
            long? nextCursor = rows.Count > limit ? page[^1].Id : null;
            var names = await ResolveActorNames(page.Select(p => p.ActorUserId).Where(id => !string.IsNullOrWhiteSpace(id))!);

            var items = page.Select(r => new
            {
                id = r.Id,
                entityType = r.EntityType,
                entityId = r.EntityId,
                parentEntityType = r.ParentEntityType,
                parentEntityId = r.ParentEntityId,
                action = r.Action,
                changes = ParseChanges(r.ChangesJson),
                actorUserId = r.ActorUserId,
                actorName = r.ActorUserId != null && names.TryGetValue(r.ActorUserId, out var n) ? n : null,
                actorKind = r.ActorKind,
                correlationId = r.CorrelationId,
                occurredAt = r.OccurredAt
            });

            return Ok(new { items, nextCursor, windowDays });
        }

        [RequirePermission(SystemPermissions.AuditLogsView)]
        [HttpGet("activity/export")]
        [AuditDataAccess("activity.export", "data.export")]
        public async Task<IActionResult> ExportActivity(
            [FromQuery] string? entityType = null,
            [FromQuery] int? entityId = null,
            [FromQuery] bool includeChildren = false,
            [FromQuery] string? actorUserId = null,
            [FromQuery] DateTimeOffset? from = null,
            [FromQuery] DateTimeOffset? to = null)
        {
            var companyId = _tenant.RequireCompanyId();
            if (AuditHistoryAccess.Evaluate(_entitlements, companyId) != AuditHistoryAccessLevel.Full)
                return FeatureNotEntitled(FeatureKeys.AuditHistoryFull,
                    "Full activity export is not available for this workspace.");

            var singleRecord = !string.IsNullOrWhiteSpace(entityType) && entityId.HasValue;
            if (!singleRecord)
            {
                // Export uses the same filters as /activity but never the 90-day window — Full only.
            }

            var query = BuildActivityQuery(companyId, entityType, entityId, includeChildren, actorUserId, from, to, cursor: null);
            var rows = await query
                .OrderByDescending(r => r.Id)
                .Take(ExportRowCap)
                .ToListAsync();

            var names = await ResolveActorNames(rows.Select(p => p.ActorUserId).Where(id => !string.IsNullOrWhiteSpace(id))!);
            var csv = BuildActivityCsv(rows, names);
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
            return File(bytes, "text/csv; charset=utf-8", "activity-export.csv");
        }

        [RequirePermission(SystemPermissions.AuditLogsView)]
        [HttpGet("security-events")]
        public async Task<IActionResult> SecurityEvents(
            [FromQuery] string? type = null,
            [FromQuery] string? actorUserId = null,
            [FromQuery] DateTimeOffset? from = null,
            [FromQuery] DateTimeOffset? to = null,
            [FromQuery] int? cursor = null,
            [FromQuery] int limit = 50)
        {
            var companyId = _tenant.RequireCompanyId();
            if (AuditHistoryAccess.Evaluate(_entitlements, companyId) != AuditHistoryAccessLevel.Full)
                return FeatureNotEntitled(FeatureKeys.AuditHistoryFull,
                    "Security events are not available for this workspace.");

            limit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 100);
            var query = _identity.SecurityAuditEvents.AsNoTracking()
                .Where(e => e.CompanyId == companyId);
            if (!string.IsNullOrWhiteSpace(type))
                query = query.Where(e => e.EventType == type);
            if (!string.IsNullOrWhiteSpace(actorUserId))
                query = query.Where(e => e.UserId == actorUserId);
            if (from.HasValue)
                query = query.Where(e => e.CreatedAt >= from.Value.UtcDateTime);
            if (to.HasValue)
                query = query.Where(e => e.CreatedAt <= to.Value.UtcDateTime);
            if (cursor.HasValue)
                query = query.Where(e => e.Id < cursor.Value);

            var rows = await query
                .OrderByDescending(e => e.Id)
                .Take(limit + 1)
                .ToListAsync();
            var page = rows.Take(limit).ToList();
            int? nextCursor = rows.Count > limit ? page[^1].Id : null;
            var names = await ResolveActorNames(page.Select(p => p.UserId).Where(id => !string.IsNullOrWhiteSpace(id))!);

            var items = page.Select(e => new
            {
                id = e.Id,
                type = e.EventType,
                outcome = e.Outcome,
                actorUserId = e.UserId,
                actorName = e.UserId != null && names.TryGetValue(e.UserId, out var n) ? n : null,
                correlationId = e.CorrelationId,
                ipAddress = e.IpAddress,
                userAgent = e.UserAgent,
                metadata = ParseMetadata(e.MetadataJson),
                createdAt = e.CreatedAt
            });

            return Ok(new { items, nextCursor });
        }

        private IQueryable<RecordChange> BuildActivityQuery(
            int companyId,
            string? entityType,
            int? entityId,
            bool includeChildren,
            string? actorUserId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            long? cursor)
        {
            var query = _db.RecordChanges.AsNoTracking().Where(r => r.CompanyId == companyId);
            if (!string.IsNullOrWhiteSpace(entityType) && entityId.HasValue && includeChildren)
            {
                var type = entityType;
                var id = entityId.Value;
                query = query.Where(r =>
                    (r.EntityType == type && r.EntityId == id)
                    || (r.ParentEntityType == type && r.ParentEntityId == id));
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(entityType))
                    query = query.Where(r => r.EntityType == entityType);
                if (entityId.HasValue)
                    query = query.Where(r => r.EntityId == entityId.Value);
            }
            if (!string.IsNullOrWhiteSpace(actorUserId))
                query = query.Where(r => r.ActorUserId == actorUserId);
            if (from.HasValue)
                query = query.Where(r => r.OccurredAt >= from.Value.UtcDateTime);
            if (to.HasValue)
                query = query.Where(r => r.OccurredAt <= to.Value.UtcDateTime);
            if (cursor.HasValue)
                query = query.Where(r => r.Id < cursor.Value);
            return query;
        }

        private static string BuildActivityCsv(IReadOnlyList<RecordChange> rows, IReadOnlyDictionary<string, string> names)
        {
            var builder = new StringBuilder();
            builder.AppendLine("occurredAt,actor,actorKind,entityType,entityId,action,field,oldValue,newValue,correlationId");
            foreach (var row in rows)
            {
                var actor = row.ActorUserId != null && names.TryGetValue(row.ActorUserId, out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name
                    : row.ActorUserId ?? string.Empty;
                var occurredAt = DateTime.SpecifyKind(row.OccurredAt, DateTimeKind.Utc)
                    .ToString("o", CultureInfo.InvariantCulture);
                foreach (var field in ExpandChangeFields(row.ChangesJson))
                {
                    builder.Append(Csv(occurredAt)).Append(',')
                        .Append(Csv(actor)).Append(',')
                        .Append(Csv(row.ActorKind)).Append(',')
                        .Append(Csv(row.EntityType)).Append(',')
                        .Append(Csv(row.EntityId.ToString(CultureInfo.InvariantCulture))).Append(',')
                        .Append(Csv(row.Action)).Append(',')
                        .Append(Csv(field.Field)).Append(',')
                        .Append(Csv(field.OldValue)).Append(',')
                        .Append(Csv(field.NewValue)).Append(',')
                        .Append(Csv(row.CorrelationId))
                        .AppendLine();
                }
            }
            return builder.ToString();
        }

        private sealed record ChangeField(string Field, string OldValue, string NewValue);

        private static IEnumerable<ChangeField> ExpandChangeFields(string? changesJson)
        {
            if (string.IsNullOrWhiteSpace(changesJson))
                yield break;
            JToken token;
            try { token = JToken.Parse(changesJson); }
            catch (Newtonsoft.Json.JsonException) { yield break; }
            if (token is not JObject obj)
                yield break;
            foreach (var property in obj.Properties())
            {
                if (property.Value is JArray { Count: >= 2 } pair)
                {
                    yield return new ChangeField(property.Name, FormatCsvValue(pair[0]), FormatCsvValue(pair[1]));
                    continue;
                }
                if (property.Value is JObject named
                    && named.TryGetValue("old", StringComparison.OrdinalIgnoreCase, out var oldToken)
                    && named.TryGetValue("new", StringComparison.OrdinalIgnoreCase, out var newToken))
                {
                    yield return new ChangeField(property.Name, FormatCsvValue(oldToken), FormatCsvValue(newToken));
                    continue;
                }
                // Added / Deleted snapshots store a single value per field.
                yield return new ChangeField(property.Name, string.Empty, FormatCsvValue(property.Value));
            }
        }

        private static string FormatCsvValue(JToken? token)
        {
            if (token is null || token.Type == JTokenType.Null) return string.Empty;
            if (token.Type == JTokenType.Boolean) return token.Value<bool>() ? "true" : "false";
            return token.Type is JTokenType.String or JTokenType.Integer or JTokenType.Float or JTokenType.Date
                ? token.ToString()
                : token.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string Csv(string? value)
        {
            var text = NeutraliseCsvFormula(value ?? string.Empty);
            if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
                return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
            return text;
        }

        /// <summary>
        /// Prefix formula-like cells so spreadsheet apps do not execute them.
        /// Leading tab and carriage return are included: Excel/Sheets can treat those as formulas too.
        /// </summary>
        public static string NeutraliseCsvFormula(string value)
        {
            if (value.Length == 0) return value;
            var first = value[0];
            if (first is '=' or '+' or '-' or '@' or '\t' or '\r')
                return "'" + value;
            return value;
        }

        private IActionResult FeatureNotEntitled(string requiredFeature, string detail)
        {
            var problem = EntitlementProblemFactory.FromDecision(
                new EntitlementDecision(
                    false,
                    "FEATURE_NOT_ENTITLED",
                    detail,
                    RequiredFeature: requiredFeature),
                _tenant);
            return new ObjectResult(problem) { StatusCode = StatusCodes.Status403Forbidden };
        }

        private async Task<Dictionary<string, string>> ResolveActorNames(IEnumerable<string?> userIds)
        {
            var ids = userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Cast<string>().ToList();
            if (ids.Count == 0) return new Dictionary<string, string>(StringComparer.Ordinal);

            var users = await _identity.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, u.Email })
                .ToListAsync();
            var emails = users.Select(u => u.Email).Where(e => !string.IsNullOrWhiteSpace(e)).Cast<string>().ToList();
            var profiles = emails.Count == 0
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : (await _db.Users.AsNoTracking()
                    .Where(u => emails.Contains(u.EmailAddress))
                    .Select(u => new { u.EmailAddress, u.Firstname, u.Lastname })
                    .ToListAsync())
                    .ToDictionary(
                        u => u.EmailAddress,
                        u => $"{u.Firstname} {u.Lastname}".Trim(),
                        StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var user in users)
            {
                if (!string.IsNullOrWhiteSpace(user.Email) && profiles.TryGetValue(user.Email, out var profileName)
                    && !string.IsNullOrWhiteSpace(profileName))
                    result[user.Id] = profileName;
                else
                    result[user.Id] = user.Email ?? user.Id;
            }
            return result;
        }

        // MVC serializes with Newtonsoft.Json, which cannot round-trip a boxed System.Text.Json.JsonElement.
        private static object? ParseChanges(string? json) => ParseJson(json);

        private static object? ParseMetadata(string? json) => ParseJson(json);

        private static object? ParseJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return Newtonsoft.Json.Linq.JToken.Parse(json); }
            catch (Newtonsoft.Json.JsonException) { return json; }
        }
    }
}
