// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Globalization;
using System.Text.Json;
using Api.Data;
using Core.Domain.Auditing;

namespace Api.Service
{
    /// <summary>
    /// Immutable collaboration evidence appended whenever workspace membership, invitation,
    /// or ownership transitions occur.
    /// </summary>
    public static class WorkspaceCollaborationEvidence
    {
        public const string AuditTableName = "__WorkspaceCollaborationEvidence";

        public const string EventMemberAdded = "MemberAdded";
        public const string EventMemberRemoved = "MemberRemoved";
        public const string EventMemberRoleUpdated = "MemberRoleUpdated";
        public const string EventInvitationAccepted = "InvitationAccepted";
        public const string EventOwnershipTransferred = "OwnershipTransferred";
        public const string EventAccountDeleted = "AccountDeleted";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public static void Record(
            ApiDbContext db,
            int companyId,
            string eventType,
            string actorUserId,
            string? targetUserId = null,
            object? details = null)
        {
            var payload = details != null
                ? JsonSerializer.Serialize(details, JsonOptions)
                : string.Empty;

            db.AuditLogs.Add(new AuditLog
            {
                CompanyId = companyId,
                UserName = actorUserId,
                AuditEventDateUTC = DateTime.UtcNow,
                AuditEventType = (int)AuditEventTypes.Added,
                TableName = AuditTableName,
                RecordId = targetUserId ?? companyId.ToString(CultureInfo.InvariantCulture),
                FieldName = eventType,
                OriginalValue = eventType,
                NewValue = payload
            });
        }
    }
}
