// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;

namespace Core.Entitlements
{
    public enum AuditHistoryAccessLevel
    {
        SingleRecordOnly = 0,
        Recent90Days = 1,
        Full = 2
    }

    /// <summary>Viewer packaging for activity and security-event APIs (recording is always on).</summary>
    public static class AuditHistoryAccess
    {
        public const int RecentWindowDays = 90;

        public static AuditHistoryAccessLevel Evaluate(IEntitlementService entitlements, int companyId)
        {
            ArgumentNullException.ThrowIfNull(entitlements);
            var snap = entitlements.Get(companyId);
            if (string.Equals(snap.EffectivePlan, PlanKeys.Community, StringComparison.OrdinalIgnoreCase)
                || string.Equals(snap.Plan, PlanKeys.Community, StringComparison.OrdinalIgnoreCase)
                || snap.HasFeature(FeatureKeys.AuditHistoryFull))
                return AuditHistoryAccessLevel.Full;

            if (snap.HasFeature(FeatureKeys.AuditActivity))
                return AuditHistoryAccessLevel.Recent90Days;

            return AuditHistoryAccessLevel.SingleRecordOnly;
        }
    }
}
