// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Api.Service;
using Core.Domain.Security;
using Xunit;

namespace AccountGo.BackendTests.Authorization;

public class WorkspaceFullAccessManifestTests
{
    [Fact]
    public void Manifest_includes_entitled_workspace_operations_without_role_admin()
    {
        Assert.Contains(SystemPermissions.FinancialsJournalPost, WorkspaceFullAccessManifest.IncludedPermissions);
        Assert.Contains(SystemPermissions.SalesInvoicesCreate, WorkspaceFullAccessManifest.IncludedPermissions);
        Assert.Contains(SystemPermissions.AdminMembersManage, WorkspaceFullAccessManifest.IncludedPermissions);
        Assert.DoesNotContain(SystemPermissions.AdminRolesManage, WorkspaceFullAccessManifest.IncludedPermissions);
        Assert.Contains(SystemPermissions.AdminRolesManage, WorkspaceFullAccessManifest.ExcludedPermissions);
    }

    [Fact]
    public void Manifest_partitions_all_system_permissions_exhaustively()
    {
        var all = typeof(SystemPermissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(fi => fi.IsLiteral && !fi.IsInitOnly && fi.FieldType == typeof(string))
            .Select(fi => (string)fi.GetValue(null)!)
            .ToHashSet(StringComparer.Ordinal);

        var included = WorkspaceFullAccessManifest.IncludedPermissions.ToHashSet(StringComparer.Ordinal);
        var excluded = WorkspaceFullAccessManifest.ExcludedPermissions.ToHashSet(StringComparer.Ordinal);

        // Every SystemPermissions constant is in exactly one of Included or Excluded
        foreach (var perm in all)
        {
            var inIncluded = included.Contains(perm);
            var inExcluded = excluded.Contains(perm);
            Assert.True(inIncluded ^ inExcluded, $"Permission '{perm}' must belong to either Included or Excluded permissions, but not both.");
            Assert.True(WorkspaceFullAccessManifest.IsClassified(perm), $"Permission '{perm}' must be classified.");
        }

        // Union of Included and Excluded equals all SystemPermissions constants
        var union = new HashSet<string>(included, StringComparer.Ordinal);
        union.UnionWith(excluded);
        Assert.Equal(all.OrderBy(x => x), union.OrderBy(x => x));

        // Intersection is empty
        Assert.Empty(included.Intersect(excluded));
    }

    [Fact]
    public void Manifest_never_marks_excluded_permissions_as_included()
    {
        foreach (var excluded in WorkspaceFullAccessManifest.ExcludedPermissions)
        {
            Assert.True(WorkspaceFullAccessManifest.IsExcluded(excluded));
            Assert.False(WorkspaceFullAccessManifest.IsIncluded(excluded));
        }
    }

    private class TestContributor : IWorkspaceAccessProfilePermissionContributor
    {
        public string ProfileKey => WorkspaceAccessProfileKeys.FullWorkspaceAccessV1;
        public IReadOnlyList<string> AdditionalWorkspacePermissions { get; init; } = Array.Empty<string>();
    }

    [Fact]
    public void Contributor_cannot_add_excluded_permission()
    {
        var contributor = new TestContributor { AdditionalWorkspacePermissions = new[] { SystemPermissions.AdminRolesManage } };
        var catalog = new WorkspaceAccessProfileCatalog(null!, null!, new[] { contributor });
        var ex = Assert.Throws<InvalidOperationException>(() => catalog.GetEffectivePermissionNames());
        Assert.Contains("excluded permission", ex.Message);
    }

    [Fact]
    public void Contributor_cannot_add_unclassified_permission()
    {
        var contributor = new TestContributor { AdditionalWorkspacePermissions = new[] { "custom.unclassified.perm" } };
        var catalog = new WorkspaceAccessProfileCatalog(null!, null!, new[] { contributor });
        var ex = Assert.Throws<InvalidOperationException>(() => catalog.GetEffectivePermissionNames());
        Assert.Contains("unclassified permission", ex.Message);
    }

    [Fact]
    public void Individual_equity_mapping_is_finance_approved_for_personal_bookkeeping()
    {
        Assert.True(Core.Domain.EntityEquityMapping.IsIndividualMappingApproved);
        var profile = Core.Domain.EntityEquityMapping.GetProfile(Core.Domain.EntityType.Individual);
        Assert.Equal("30100", profile.CloseTargetAccountCode);
        Assert.Null(profile.DistributionAccountCode);
        Assert.Equal("Accumulated net worth", profile.DisplayNames["30100"]);
        Assert.False(profile.DisplayNames.ContainsKey("30800"));
    }
}
