// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;

namespace Core.Entitlements
{
    /// <summary>
    /// Compound gate for statement import (#436). Shared by
    /// <see cref="IEntitlementService.EvaluateStatementImport"/> and tests so Moq
    /// implementations that do not surface default interface methods still cover the rules.
    /// </summary>
    public static class StatementImportAccess
    {
        /// <param name="financialsAccess">
        /// Read for GET/HEAD (browsing imports); Write for mutating actions so a read-only
        /// financials grace period cannot commit/match statement lines (#436 I1 should-fix).
        /// </param>
        public static EntitlementDecision Evaluate(
            IEntitlementService entitlements,
            int companyId,
            ModuleAccess financialsAccess = ModuleAccess.Write)
        {
            ArgumentNullException.ThrowIfNull(entitlements);

            var financials = entitlements.Evaluate(
                companyId,
                new EntitlementAction.Module(financialsAccess, "financials"));
            if (financials.Allowed)
            {
                return new EntitlementDecision(
                    true, null, null,
                    RequiredModule: "financials",
                    EffectivePlan: financials.EffectivePlan,
                    UpgradeAvailable: false,
                    SubscriptionStatus: financials.SubscriptionStatus);
            }

            var snap = entitlements.Get(companyId);
            if (string.Equals(snap.EffectivePlan, PlanKeys.Community, StringComparison.OrdinalIgnoreCase)
                || string.Equals(snap.Plan, PlanKeys.Community, StringComparison.OrdinalIgnoreCase))
            {
                return new EntitlementDecision(
                    true, null, null,
                    EffectivePlan: PlanKeys.Community,
                    UpgradeAvailable: false);
            }

            return entitlements.Evaluate(
                companyId,
                new EntitlementAction.Feature(FeatureKeys.BankingStatementImport));
        }
    }
}
