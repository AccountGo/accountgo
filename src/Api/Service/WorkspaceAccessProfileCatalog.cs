// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Api.Data;
using Core.Authorization;
using Core.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Services.Security;

namespace Api.Service
{
    public sealed class WorkspaceAccessProfileCatalog : IWorkspaceAccessProfileCatalog
    {
        private readonly ApiDbContext _db;
        private readonly ISecurityService _securityService;
        private readonly IEnumerable<IWorkspaceAccessProfilePermissionContributor> _contributors;

        public WorkspaceAccessProfileCatalog(
            ApiDbContext db,
            ISecurityService securityService,
            IEnumerable<IWorkspaceAccessProfilePermissionContributor> contributors)
        {
            _db = db;
            _securityService = securityService;
            _contributors = contributors;
        }

        public string FullAccessProfileKey => WorkspaceAccessProfileKeys.FullWorkspaceAccessV1;
        public string FullAccessDisplayName => WorkspaceAccessProfileKeys.FullWorkspaceAccessDisplayName;
        public int FullAccessProfileVersion => WorkspaceAccessProfileKeys.FullWorkspaceAccessProfileVersion;

        public void EnsureSeeded() => _securityService.EnsureDefaultPermissionsAndRoles();

        public Core.Domain.Security.SecurityRole? FindFullAccessRole() =>
            _db.SecurityRoles.AsNoTracking()
                .FirstOrDefault(r => r.Name == WorkspaceAccessProfileKeys.FullWorkspaceAccessV1);

        public IReadOnlyList<string> GetEffectivePermissionNames()
        {
            var names = new HashSet<string>(WorkspaceFullAccessManifest.IncludedPermissions, StringComparer.Ordinal);

            foreach (var contributor in _contributors)
            {
                if (!string.Equals(contributor.ProfileKey, FullAccessProfileKey, StringComparison.Ordinal))
                    continue;

                foreach (var permission in contributor.AdditionalWorkspacePermissions)
                {
                    if (string.IsNullOrWhiteSpace(permission))
                        continue;
                    if (WorkspaceFullAccessManifest.IsExcluded(permission))
                        throw new InvalidOperationException(
                            $"Workspace access profile contributor attempted to add excluded permission '{permission}'.");
                    if (!WorkspaceFullAccessManifest.IsClassified(permission))
                        throw new InvalidOperationException(
                            $"Workspace access profile contributor attempted to add unclassified permission '{permission}'.");
                    names.Add(permission);
                }
            }

            return names.OrderBy(n => n, StringComparer.Ordinal).ToList();
        }
    }
}
