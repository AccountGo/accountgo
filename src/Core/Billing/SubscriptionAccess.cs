// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Core.Domain;

namespace Core.Billing
{
    /// <summary>
    /// Shared rules for whether a workspace subscription still grants paid-module access.
    /// Grace matches the processor dunning window (14 days after period end while past_due).
    /// </summary>
    public static class SubscriptionAccess
    {
        public static readonly TimeSpan PastDueGrace = TimeSpan.FromDays(14);

        public static bool IsInactive(Company company, DateTime? utcNow = null)
        {
            ArgumentNullException.ThrowIfNull(company);
            var now = utcNow ?? DateTime.UtcNow;
            var status = (company.SubscriptionStatus ?? "active").Trim().ToLowerInvariant();

            if (status is "canceled" or "unpaid" or "incomplete_expired")
                return true;

            if (status != "past_due")
                return false;

            var periodEnd = company.CurrentPeriodEnd ?? company.SubscriptionValidUntil;
            if (periodEnd == null)
                return true;

            return now > periodEnd.Value.ToUniversalTime().Add(PastDueGrace);
        }
    }
}
