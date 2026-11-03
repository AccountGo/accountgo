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
    /// Immutable origin evidence for workspace creation. Workspaces created through application
    /// creation receive this record; backfilled or migrated legacy workspaces do not.
    /// </summary>
    public static class WorkspaceOriginEvidence
    {
        public const string AuditTableName = "__WorkspaceOrigin";
        public const string OriginKindApplicationCreate = "ApplicationCreate";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public static void Record(
            ApiDbContext db,
            int companyId,
            string creatingUserId,
            string originKind = OriginKindApplicationCreate)
        {
            var payload = JsonSerializer.Serialize(new
            {
                companyId,
                creatingUserId,
                originKind,
                createdAtUtc = DateTime.UtcNow
            }, JsonOptions);

            db.AuditLogs.Add(new AuditLog
            {
                CompanyId = companyId,
                UserName = creatingUserId,
                AuditEventDateUTC = DateTime.UtcNow,
                AuditEventType = (int)AuditEventTypes.Added,
                TableName = AuditTableName,
                RecordId = companyId.ToString(CultureInfo.InvariantCulture),
                FieldName = "Origin",
                OriginalValue = originKind,
                NewValue = payload
            });
        }

        public static bool HasValidOrigin(ApiDbContext db, int companyId, string creatingUserId)
        {
            return db.AuditLogs
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Any(a => a.CompanyId == companyId
                          && a.TableName == AuditTableName
                          && a.RecordId == companyId.ToString(CultureInfo.InvariantCulture)
                          && a.UserName == creatingUserId
                          && a.OriginalValue == OriginKindApplicationCreate);
        }
    }
}
