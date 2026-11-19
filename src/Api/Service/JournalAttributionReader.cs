// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Api.Data;
using Dto.Financial;
using Microsoft.EntityFrameworkCore;

namespace Api.Service
{
    /// <summary>
    /// Reads who prepared and who posted a manual journal from the evidence the app already keeps: the preparer rows
    /// written on every save, and the journal's own change history for the moment it was posted.
    /// </summary>
    public sealed class JournalAttributionReader
    {
        private readonly ApiDbContext _db;
        private readonly ApplicationIdentityDbContext _identity;

        public JournalAttributionReader(ApiDbContext db, ApplicationIdentityDbContext identity)
        {
            _db = db;
            _identity = identity;
        }

        public JournalAttribution Read(int companyId, int journalId)
        {
            var recordId = journalId.ToString(CultureInfo.InvariantCulture);
            var preparerRows = _db.AuditLogs.AsNoTracking()
                .Where(a => a.CompanyId == companyId && a.TableName == "__JournalEntryPreparer" && a.RecordId == recordId)
                .Select(a => new { a.UserName, a.AuditEventDateUTC })
                .ToList();

            // The journal's history holds one row per save; the posting save is the one that turned Posted on.
            var changes = _db.RecordChanges.AsNoTracking()
                .Where(c => c.CompanyId == companyId && c.EntityType == "JournalEntryHeader" && c.EntityId == journalId
                    && (c.Action == "Added" || c.Action == "Modified"))
                .OrderBy(c => c.Id)
                .Select(c => new { c.Action, c.ChangesJson, c.ActorUserId, c.OccurredAt })
                .ToList();
            var posting = changes.LastOrDefault(c => TurnedPostedOn(c.Action, c.ChangesJson));

            var names = ResolveNames(preparerRows.Select(p => p.UserName).Append(posting?.ActorUserId));
            string? NameOf(string? userId) => userId != null && names.TryGetValue(userId, out var name) ? name : null;

            var preparers = preparerRows
                .Where(p => !string.IsNullOrWhiteSpace(p.UserName))
                .GroupBy(p => p.UserName, StringComparer.Ordinal)
                .Select(g => new JournalActor
                {
                    UserId = g.Key!,
                    Name = NameOf(g.Key),
                    FirstAt = g.Min(p => p.AuditEventDateUTC),
                    LastAt = g.Max(p => p.AuditEventDateUTC)
                })
                .OrderBy(p => p.FirstAt)
                .ToList();

            return new JournalAttribution
            {
                PreparedBy = preparers,
                PostedByUserId = posting?.ActorUserId,
                PostedByName = NameOf(posting?.ActorUserId),
                PostedAt = posting?.OccurredAt,
                PostedByAPreparer = posting?.ActorUserId == null
                    ? null
                    : preparers.Any(p => string.Equals(p.UserId, posting.ActorUserId, StringComparison.Ordinal))
            };
        }

        /// <summary>True when a change row records Posted becoming true: on create, or as an old/new pair on update.</summary>
        internal static bool TurnedPostedOn(string action, string? changesJson)
        {
            if (string.IsNullOrWhiteSpace(changesJson)) return false;
            try
            {
                using var doc = JsonDocument.Parse(changesJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object
                    || !doc.RootElement.TryGetProperty("Posted", out var posted))
                    return false;
                if (action == "Added")
                    return posted.ValueKind == JsonValueKind.True;
                return posted.ValueKind == JsonValueKind.Array
                    && posted.GetArrayLength() == 2
                    && posted[1].ValueKind == JsonValueKind.True
                    && posted[0].ValueKind != JsonValueKind.True;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private Dictionary<string, string> ResolveNames(IEnumerable<string?> userIds)
        {
            var ids = userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Cast<string>().ToList();
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (ids.Count == 0) return result;

            var users = _identity.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.Email }).ToList();
            var emails = users.Select(u => u.Email).Where(e => !string.IsNullOrWhiteSpace(e)).Cast<string>().ToList();
            var profiles = emails.Count == 0
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : _db.Users.AsNoTracking()
                    .Where(u => u.EmailAddress != null && emails.Contains(u.EmailAddress))
                    .Select(u => new { u.EmailAddress, u.Firstname, u.Lastname })
                    .ToList()
                    .GroupBy(u => u.EmailAddress!, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => $"{g.First().Firstname} {g.First().Lastname}".Trim(), StringComparer.OrdinalIgnoreCase);

            foreach (var user in users)
            {
                result[user.Id] = !string.IsNullOrWhiteSpace(user.Email) && profiles.TryGetValue(user.Email, out var profileName)
                    && !string.IsNullOrWhiteSpace(profileName)
                    ? profileName
                    : user.Email ?? user.Id;
            }
            return result;
        }
    }
}
