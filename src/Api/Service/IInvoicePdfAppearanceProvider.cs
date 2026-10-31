// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Core.Tenancy;

namespace Api.Service;

/// <summary>
/// Resolves the invoice PDF appearance for the current organization.
/// The document renderer does not inspect plans, entitlements, or storage.
/// </summary>
public interface IInvoicePdfAppearanceProvider
{
    /// <summary>Appearance to use for a real invoice PDF.</summary>
    Task<InvoicePdfAppearance> GetEffectiveAsync(CancellationToken ct = default);

    /// <summary>
    /// Appearance for a preview. The default ignores unsaved input and returns the effective appearance.
    /// </summary>
    Task<InvoicePdfAppearance> PreviewAsync(InvoiceBrandingDesired? desired, CancellationToken ct = default) =>
        GetEffectiveAsync(ct);
}

/// <summary>
/// Self-hosted default. Always uses the built-in palette and shows AccountGo attribution.
/// A module may replace this provider. There is no configuration switch here.
/// </summary>
public sealed class DefaultInvoicePdfAppearanceProvider : IInvoicePdfAppearanceProvider
{
    public Task<InvoicePdfAppearance> GetEffectiveAsync(CancellationToken ct = default) =>
        Task.FromResult(InvoicePdfAppearance.Default);
}

/// <summary>Reads organization appearance capabilities. The default reports customization as unavailable.</summary>
public interface IInvoiceBrandingCapabilities
{
    Task<InvoiceBrandingCapabilities> GetAsync(string actorId, CancellationToken ct = default);
}

/// <summary>Community builds have no appearance settings API.</summary>
public sealed class DefaultInvoiceBrandingCapabilities(ITenantContext tenant) : IInvoiceBrandingCapabilities
{
    public Task<InvoiceBrandingCapabilities> GetAsync(string actorId, CancellationToken ct = default) =>
        Task.FromResult(new InvoiceBrandingCapabilities(
            tenant.RequireCompanyId(),
            CanCustomizeInvoiceAppearance: false,
            CanHideAccountGoBranding: false,
            CanManageSettings: false,
            UpgradeAvailable: false,
            UnavailableReason: "Invoice appearance customization is unavailable.",
            HasRetainedSettings: false,
            EffectiveShowsAccountGoBranding: true,
            InactiveReason: null));
}

/// <summary>Reads and writes organization appearance settings. Present only when a module provides it.</summary>
public interface IInvoiceBrandingSettings
{
    Task<InvoiceBrandingSettingsModel> GetAsync(string actorId, CancellationToken ct = default);
    Task<InvoiceBrandingSettingsModel> SaveAsync(string actorId, InvoiceBrandingDesired desired, CancellationToken ct = default);
    Task<InvoiceBrandingSettingsModel> RestoreDefaultsAsync(string actorId, int revision, CancellationToken ct = default);
}
