// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Core.Domain.Security;
using Core.Entitlements;

namespace Api.DocumentCaptures;

public enum DocumentCaptureAccessFailure
{
    None,
    FeatureRequired,
    ModuleRequired,
    PermissionRequired,
    KindUnavailable
}

public static class DocumentCaptureAccessPolicy
{
    public static EntitlementDecision? GetEntitlementDecision(IEntitlementService entitlements, int companyId, string kind,
        DocumentCaptureAccessFailure failure)
    {
        if (failure == DocumentCaptureAccessFailure.FeatureRequired)
            return entitlements.Evaluate(companyId, new EntitlementAction.Feature(FeatureKeys.DocumentCapture));
        var requirement = GetRequirement(kind);
        return failure == DocumentCaptureAccessFailure.ModuleRequired && requirement is { Modules.Length: > 0 }
            ? entitlements.Evaluate(companyId, new EntitlementAction.Module(ModuleAccess.Write, requirement.Modules))
            : null;
    }

    public static IReadOnlyList<string>? GetRequiredModules(string kind) => GetRequirement(kind)?.Modules;

    public static DocumentCaptureAccessFailure AuthorizeNew(
        IEntitlementService entitlements,
        int companyId,
        IReadOnlySet<string> permissions,
        string kind)
    {
        var requirement = GetRequirement(kind);
        if (requirement is null)
            return DocumentCaptureAccessFailure.KindUnavailable;

        var snapshot = entitlements.Get(companyId);
        var community = string.Equals(snapshot.Plan, PlanKeys.Community, StringComparison.OrdinalIgnoreCase)
            || string.Equals(snapshot.EffectivePlan, PlanKeys.Community, StringComparison.OrdinalIgnoreCase);
        if (!community && !snapshot.HasFeature(FeatureKeys.DocumentCapture))
            return DocumentCaptureAccessFailure.FeatureRequired;

        if (requirement.Modules.Length > 0
            && !entitlements.Evaluate(companyId, new EntitlementAction.Module(ModuleAccess.Write, requirement.Modules)).Allowed)
            return DocumentCaptureAccessFailure.ModuleRequired;
        if (requirement.WritePermissions.Any(permission => !permissions.Contains(permission)))
            return DocumentCaptureAccessFailure.PermissionRequired;
        return DocumentCaptureAccessFailure.None;
    }

    public static DocumentCaptureAccessFailure AuthorizeRead(
        IEntitlementService entitlements,
        int companyId,
        IReadOnlySet<string> permissions,
        string kind)
    {
        var requirement = GetRequirement(kind);
        if (requirement is null)
            return DocumentCaptureAccessFailure.KindUnavailable;
        if (requirement.ReadPermissions.Any(permission => !permissions.Contains(permission)))
            return DocumentCaptureAccessFailure.PermissionRequired;
        if (requirement.Modules.Length > 0
            && !entitlements.Evaluate(companyId, new EntitlementAction.Module(ModuleAccess.Read, requirement.Modules)).Allowed)
            return DocumentCaptureAccessFailure.ModuleRequired;
        return DocumentCaptureAccessFailure.None;
    }

    public static DocumentCaptureAccessFailure AuthorizeExistingWrite(
        IEntitlementService entitlements, int companyId, IReadOnlySet<string> permissions, string kind)
    {
        var requirement = GetRequirement(kind);
        if (requirement is null)
            return DocumentCaptureAccessFailure.KindUnavailable;
        if (requirement.WritePermissions.Any(permission => !permissions.Contains(permission)))
            return DocumentCaptureAccessFailure.PermissionRequired;
        return requirement.Modules.Length > 0
            && !entitlements.Evaluate(companyId, new EntitlementAction.Module(ModuleAccess.Write, requirement.Modules)).Allowed
                ? DocumentCaptureAccessFailure.ModuleRequired
                : DocumentCaptureAccessFailure.None;
    }

    private static KindRequirement? GetRequirement(string kind) => kind switch
    {
        "expense-receipt" or "purchase-invoice" => new(
            [SystemModules.Purchases],
            [SystemPermissions.PurchasesBillsCreate],
            [SystemPermissions.PurchasesBillsView]),
        "sales-invoice" => new(
            [SystemModules.Sales],
            [SystemPermissions.SalesInvoicesCreate],
            [SystemPermissions.SalesInvoicesView]),
        "bank" => new(
            [SystemModules.Sales, SystemModules.Purchases, SystemModules.Financials],
            [SystemPermissions.BankingAccountsManage],
            [SystemPermissions.BankingAccountsManage, SystemPermissions.BankingAccountsView]),
        "assistant-attachment" => new([], [], []),
        _ => null
    };

    private sealed record KindRequirement(string[] Modules, string[] WritePermissions, string[] ReadPermissions);
}
