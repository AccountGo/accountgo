// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Api.Data;
using Core.Domain;
using Core.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Api.Service
{
    /// <summary>
    /// Persists invitation transition metadata so acceptance can reject stale grant/policy assumptions.
    /// Stored as append-only audit evidence (no invitation-table migration in this pass).
    /// </summary>
    public static class WorkspaceInvitationTransition
    {
        public const string AuditTableName = "__InvitationTransition";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public static string ComputeGrantFingerprint(CompanyMemberRole role, IEnumerable<int> securityRoleIds)
        {
            var roles = string.Join(',', securityRoleIds.Distinct().OrderBy(id => id));
            var material = $"{(int)role}|{roles}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
            return Convert.ToHexString(hash);
        }

        public static void Record(
            ApiDbContext db,
            int companyId,
            int invitationId,
            string actorUserId,
            CompanyMemberRole role,
            IReadOnlyList<int> securityRoleIds,
            long issuingPolicyRevision,
            string? intendedJournalMode)
        {
            var fingerprint = ComputeGrantFingerprint(role, securityRoleIds);
            var payload = JsonSerializer.Serialize(new
            {
                invitationId,
                role = role.ToString(),
                securityRoleIds = securityRoleIds.Distinct().OrderBy(id => id).ToArray(),
                grantFingerprint = fingerprint,
                issuingPolicyRevision,
                intendedJournalMode
            }, JsonOptions);

            db.AuditLogs.Add(new AuditLog
            {
                CompanyId = companyId,
                UserName = actorUserId,
                AuditEventDateUTC = DateTime.UtcNow,
                AuditEventType = (int)AuditEventTypes.Added,
                TableName = AuditTableName,
                RecordId = invitationId.ToString(CultureInfo.InvariantCulture),
                FieldName = "Transition",
                OriginalValue = fingerprint,
                NewValue = payload
            });
        }

        public static LatestTransitionInfo? GetLatest(ApiDbContext db, int companyId, int invitationId)
        {
            var row = db.AuditLogs
                .AsNoTracking()
                .Where(a => a.CompanyId == companyId
                            && a.TableName == AuditTableName
                            && a.RecordId == invitationId.ToString(CultureInfo.InvariantCulture))
                .OrderByDescending(a => a.Id)
                .FirstOrDefault();

            if (row == null || string.IsNullOrWhiteSpace(row.NewValue))
                return null;

            try
            {
                var payload = JsonSerializer.Deserialize<InvitationTransitionPayload>(row.NewValue, JsonOptions);
                if (payload == null) return null;
                return new LatestTransitionInfo
                {
                    GrantFingerprint = payload.GrantFingerprint ?? row.OriginalValue,
                    IssuingPolicyRevision = payload.IssuingPolicyRevision,
                    IntendedJournalMode = payload.IntendedJournalMode
                };
            }
            catch
            {
                return null;
            }
        }

        public static (bool Ok, string? Title, string? Detail) ValidateOnAccept(
            ApiDbContext db,
            CompanyInvitation invitation,
            IReadOnlyList<int> securityRoleIds,
            long currentPolicyRevision)
        {
            var row = db.AuditLogs
                .AsNoTracking()
                .Where(a => a.CompanyId == invitation.CompanyId
                            && a.TableName == AuditTableName
                            && a.RecordId == invitation.Id.ToString(CultureInfo.InvariantCulture))
                .OrderByDescending(a => a.Id)
                .FirstOrDefault();

            // Legacy invitations created before transition evidence remain acceptable.
            if (row == null || string.IsNullOrWhiteSpace(row.NewValue))
                return (true, null, null);

            InvitationTransitionPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<InvitationTransitionPayload>(row.NewValue, JsonOptions);
            }
            catch
            {
                return (true, null, null);
            }

            if (payload == null)
                return (true, null, null);

            var currentFingerprint = ComputeGrantFingerprint(invitation.Role, securityRoleIds);
            if (!string.Equals(payload.GrantFingerprint, currentFingerprint, StringComparison.Ordinal)
                || !string.Equals(payload.GrantFingerprint, row.OriginalValue, StringComparison.Ordinal))
            {
                return (false, "Invitation grant changed",
                    "The invitation access selection no longer matches what was confirmed. Ask the workspace owner for a new invitation.");
            }

            // Reject when the workspace policy revision advanced since the invitation was issued.
            if (payload.IssuingPolicyRevision >= 0
                && currentPolicyRevision > payload.IssuingPolicyRevision)
            {
                return (false, "Workspace setup changed",
                    "Workspace collaboration or approval settings changed after this invitation was sent. Ask the owner to resend it.");
            }

            return (true, null, null);
        }

        public sealed class LatestTransitionInfo
        {
            public string? GrantFingerprint { get; set; }
            public long IssuingPolicyRevision { get; set; }
            public string? IntendedJournalMode { get; set; }
        }

        private sealed class InvitationTransitionPayload
        {
            public string? GrantFingerprint { get; set; }
            public long IssuingPolicyRevision { get; set; }
            public string? IntendedJournalMode { get; set; }
        }
    }
}
