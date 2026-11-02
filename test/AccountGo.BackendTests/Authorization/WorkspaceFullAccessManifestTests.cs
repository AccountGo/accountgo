// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using System.Reflection;
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
    public void Manifest_does_not_include_every_SystemPermissions_constant()
    {
        var all = typeof(SystemPermissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(fi => fi.IsLiteral && !fi.IsInitOnly && fi.FieldType == typeof(string))
            .Select(fi => (string)fi.GetValue(null)!)
            .ToList();

        Assert.True(all.Count > WorkspaceFullAccessManifest.IncludedPermissions.Count);
        Assert.All(WorkspaceFullAccessManifest.ExcludedPermissions, excluded =>
            Assert.DoesNotContain(excluded, WorkspaceFullAccessManifest.IncludedPermissions));
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

    [Fact]
    public void Individual_equity_mapping_exists_but_activation_remains_gated()
    {
        Assert.Throws<InvalidOperationException>(() => Core.Domain.EntityEquityMapping.GetProfile(Core.Domain.EntityType.Individual));
        var profile = Core.Domain.EntityEquityMapping.GetProfile(Core.Domain.EntityType.Individual, allowUnapproved: true);
        Assert.Equal("30100", profile.CloseTargetAccountCode);
        Assert.Equal("30800", profile.DistributionAccountCode);
        Assert.Equal("Personal Capital", profile.DisplayNames["30100"]);
        Assert.False(Core.Domain.EntityEquityMapping.IsIndividualMappingApproved);
    }
}
