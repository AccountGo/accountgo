// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Domain.Security
{
    /// <summary>
    /// Reviewed permission set for the managed Full workspace access profile.
    /// Do not populate by reflecting all SystemPermissions or copying SysAdmin.
    /// </summary>
    public static class WorkspaceFullAccessManifest
    {
        public static readonly IReadOnlyList<string> IncludedPermissions = new[]
        {
            SystemPermissions.SalesCustomersView,
            SystemPermissions.SalesCustomersManage,
            SystemPermissions.SalesInvoicesView,
            SystemPermissions.SalesInvoicesCreate,
            SystemPermissions.SalesInvoicesPost,
            SystemPermissions.SalesReceiptsView,
            SystemPermissions.SalesReceiptsCreate,
            SystemPermissions.SalesQuotationsView,

            SystemPermissions.FinancialsAccountsView,
            SystemPermissions.FinancialsAccountsManage,
            SystemPermissions.FinancialsJournalView,
            SystemPermissions.FinancialsJournalCreate,
            SystemPermissions.FinancialsJournalPost,
            SystemPermissions.FinancialsPeriodsClose,

            SystemPermissions.BankingAccountsView,
            SystemPermissions.BankingAccountsManage,
            SystemPermissions.BankingTransactionsCreate,
            SystemPermissions.BankingReconcile,
            SystemPermissions.BankingReconcileUndo,

            SystemPermissions.PurchasesVendorsView,
            SystemPermissions.PurchasesVendorsManage,
            SystemPermissions.PurchasesOrdersView,
            SystemPermissions.PurchasesBillsView,
            SystemPermissions.PurchasesBillsCreate,
            SystemPermissions.PurchasesBillsPost,
            SystemPermissions.PurchasesPaymentsCreate,

            SystemPermissions.InventoryItemsView,
            SystemPermissions.InventoryItemsManage,
            SystemPermissions.InventoryAdjustmentsView,
            SystemPermissions.InventoryAdjustmentsPost,

            SystemPermissions.ReportsView,

            SystemPermissions.DonationsView,
            SystemPermissions.DonationsManage,

            SystemPermissions.AuditLogsView,

            SystemPermissions.AdminMembersManage,
            SystemPermissions.AdminSubscriptionManage,
            SystemPermissions.AdminTaxesManage,
        };

        /// <summary>
        /// Explicitly excluded workspace permission constants. Global role catalog editing is not
        /// part of entitled workspace operations for this profile.
        /// </summary>
        public static readonly IReadOnlyList<string> ExcludedPermissions = new[]
        {
            SystemPermissions.AdminRolesManage,
        };

        public static IEnumerable<string> ForbiddenIfPresent => ExcludedPermissions;

        public static bool IsIncluded(string permissionName) =>
            IncludedPermissions.Contains(permissionName, StringComparer.Ordinal);

        public static bool IsExcluded(string permissionName) =>
            ExcludedPermissions.Contains(permissionName, StringComparer.Ordinal);

        public static bool IsClassified(string permissionName) =>
            IsIncluded(permissionName) || IsExcluded(permissionName);
    }

    /// <summary>
    /// Optional commercial-module contribution of additional workspace-scoped permissions.
    /// Implementations must not introduce platform/staff privileges.
    /// </summary>
    public interface IWorkspaceAccessProfilePermissionContributor
    {
        string ProfileKey { get; }
        IReadOnlyList<string> AdditionalWorkspacePermissions { get; }
    }
}
