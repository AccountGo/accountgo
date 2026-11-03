// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Api.Data;
using Core.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Api.Service
{
    /// <summary>
    /// Immutable, server-issued evidence that a workspace was created by the approved signup flow and is
    /// awaiting its post-verification initialization continuation. This is deliberately NARROWER than
    /// <see cref="WorkspaceOriginEvidence"/>: generic origin evidence is written for EVERY
    /// application-created workspace (including ordinary authenticated creation and P0-1/P0-2 workspaces),
    /// so it cannot prove that a workspace is specifically pending the signup continuation. Only the signup
    /// path — which creates the workspace unclassified and leaves initialization for after email
    /// verification — writes this marker, bound to the workspace and its original owner. The authenticated
    /// create flow, which initializes in the same request, does not.
    ///
    /// The <c>current/initialize</c> continuation requires this marker under the coordination lock, so a
    /// pristine but non-signup workspace (e.g. one created directly through the authenticated API and left
    /// uninitialized) can never be initialized through that endpoint.
    /// </summary>
    public static class WorkspaceSignupContinuationEvidence
    {
        public const string AuditTableName = "__WorkspaceSignupContinuation";
        public const string EventPending = "PendingSignupContinuation";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Records the pending-continuation marker inside the signup transaction, bound to the company and
        /// the original owner's user id. Call exactly once, when the signup flow creates the unclassified
        /// workspace it will not initialize until the owner verifies their email.
        /// </summary>
        public static void Record(ApiDbContext db, int companyId, string originalOwnerUserId)
        {
            var payload = JsonSerializer.Serialize(new
            {
                companyId,
                originalOwnerUserId,
                createdAtUtc = DateTime.UtcNow
            }, JsonOptions);

            db.AuditLogs.Add(new AuditLog
            {
                CompanyId = companyId,
                UserName = originalOwnerUserId,
                AuditEventDateUTC = DateTime.UtcNow,
                AuditEventType = (int)AuditEventTypes.Added,
                TableName = AuditTableName,
                RecordId = companyId.ToString(CultureInfo.InvariantCulture),
                FieldName = EventPending,
                OriginalValue = EventPending,
                NewValue = payload
            });
        }

        /// <summary>
        /// True when the workspace carries a pending-signup-continuation marker bound to
        /// <paramref name="ownerUserId"/>. Read under the coordination lock as part of the continuation
        /// eligibility proof.
        /// </summary>
        public static bool IsPendingContinuation(ApiDbContext db, int companyId, string ownerUserId)
        {
            return db.AuditLogs
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Any(a => a.CompanyId == companyId
                          && a.TableName == AuditTableName
                          && a.RecordId == companyId.ToString(CultureInfo.InvariantCulture)
                          && a.UserName == ownerUserId
                          && a.OriginalValue == EventPending);
        }
    }
}
