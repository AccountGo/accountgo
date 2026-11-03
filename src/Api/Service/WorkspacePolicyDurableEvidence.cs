// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Linq;
using Api.Data;
using Core.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Api.Service
{
    /// <summary>
    /// Read helpers over the durable workspace-policy audit markers. Violation deduplication and current
    /// state now live in <see cref="WorkspaceInvariantIncidentLog"/>; this type only exposes convenience
    /// reads used by diagnostics and tests.
    /// </summary>
    public static class WorkspacePolicyDurableEvidence
    {
        /// <summary>True when a first-useful-action marker already exists for the workspace.</summary>
        public static bool HasFirstUsefulAction(ApiDbContext db, int companyId) =>
            db.AuditLogs.IgnoreQueryFilters()
                .Any(a => a.CompanyId == companyId
                          && a.TableName == WorkspaceFirstActionEvidence.FirstUsefulActionTable);
    }
}
