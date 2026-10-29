// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Billing
{
    /// <summary>
    /// Result of requesting a plan change. Community applies immediately;
    /// hosted providers return a checkout/portal redirect URL and leave Plan untouched until webhook.
    /// </summary>
    public sealed record PlanChangeResult(bool AppliedImmediately, string? RedirectUrl, string? ErrorReason);

    /// <summary>
    /// Workspace billing snapshot returned by <c>GET /api/billing/state</c>.
    /// Limit values and feature lists come from the entitlement service; this DTO is the UI contract.
    /// </summary>
    public sealed record BillingStateDto(
        bool BillingEnabled,
        string Plan,
        string Tier,
        string Status,
        DateTime? CurrentPeriodEnd,
        bool CancelAtPeriodEnd,
        IReadOnlyDictionary<string, object?> Limits,
        IReadOnlyList<string> Features,
        bool PortalAvailable);

    /// <summary>
    /// Pluggable billing backend. Community default applies plan changes immediately with no payment provider.
    /// A module may replace this registration.
    /// </summary>
    public interface IBillingProvider
    {
        /// <summary>
        /// Self-serve Checkout / Upgrade UI. Each implementation decides when this is enabled.
        /// Community: always false (immediate plan switches).
        /// </summary>
        bool Enabled { get; }

        /// <summary>
        /// When true, paid plan changes must go through <see cref="RequestPlanChangeAsync"/>
        /// (never apply immediately). A provider-backed implementation may return true even when
        /// <see cref="Enabled"/> is false (for example, behind its own kill switch).
        /// </summary>
        bool RequiresProviderCheckout { get; }

        Task<PlanChangeResult> RequestPlanChangeAsync(
            int companyId,
            string planKey,
            string returnUrl,
            CancellationToken ct);

        Task<string?> GetPortalUrlAsync(int companyId, string returnUrl, CancellationToken ct);

        Task<BillingStateDto> GetBillingStateAsync(int companyId, CancellationToken ct);
    }
}
