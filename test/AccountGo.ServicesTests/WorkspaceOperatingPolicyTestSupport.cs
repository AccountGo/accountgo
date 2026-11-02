// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.Data;
using Api.Options;
using Api.Service;
using Api.Tenancy;
using Core.Authorization;
using Core.Tenancy;
using Microsoft.Extensions.Options;
using Moq;
using Services.Security;

namespace AccountGo.ServicesTests;

internal static class WorkspaceOperatingPolicyTestSupport
{
    public static WorkspaceInitializationApplicator CreateInitializer(ApiDbContext db, ITenantContext? tenant = null)
    {
        tenant ??= new TenantContext();
        var options = Options.Create(new WorkspaceExperienceOptions { SoloSetupEnabled = false });
        var policyTx = new WorkspacePolicyTransaction(db, tenant);
        var catalog = new WorkspaceAccessProfileCatalog(db, Mock.Of<ISecurityService>(), System.Array.Empty<Core.Domain.Security.IWorkspaceAccessProfilePermissionContributor>());
        var resolver = new WorkspaceApprovalPolicyResolver(db);
        return new WorkspaceInitializationApplicator(db, policyTx, catalog, resolver, options);
    }

    public static IWorkspaceOperatingPolicyService CreateOperatingPolicy(ApiDbContext db, bool soloSetupEnabled = false, ITenantContext? tenant = null)
    {
        tenant ??= new TenantContext();
        var options = Options.Create(new WorkspaceExperienceOptions { SoloSetupEnabled = soloSetupEnabled });
        var policyTx = new WorkspacePolicyTransaction(db, tenant);
        var catalog = new WorkspaceAccessProfileCatalog(db, Mock.Of<ISecurityService>(), System.Array.Empty<Core.Domain.Security.IWorkspaceAccessProfilePermissionContributor>());
        var resolver = new WorkspaceApprovalPolicyResolver(db);
        return new WorkspaceOperatingPolicyService(db, resolver, catalog, policyTx, options);
    }
}
