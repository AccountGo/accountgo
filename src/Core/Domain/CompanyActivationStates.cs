// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;

namespace Core.Domain
{
    /// <summary>Stored values for <see cref="Company.ActivationState"/>.</summary>
    public static class CompanyActivationStates
    {
        public const string Active = "active";
        public const string PendingCheckout = "pending_checkout";

        public static bool IsPendingCheckout(string? value) =>
            string.Equals(value, PendingCheckout, StringComparison.OrdinalIgnoreCase);
    }
}
