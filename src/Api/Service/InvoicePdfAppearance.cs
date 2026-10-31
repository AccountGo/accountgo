// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Api.Service;

/// <summary>
/// Print colors for one invoice PDF. The page itself stays white.
/// Attribution styling is not part of this palette.
/// </summary>
public sealed record InvoicePdfPalette(
    string Text,
    string SecondaryText,
    string MutedText,
    string Border,
    string Surface,
    string Accent,
    string Success,
    string Danger,
    string Neutral)
{
    /// <summary>Colors used by invoice PDFs before any customization is applied.</summary>
    public static InvoicePdfPalette Default { get; } = new(
        Text: "#0F172A",
        SecondaryText: "#475569",
        MutedText: "#64748B",
        Border: "#E2E8F0",
        Surface: "#F1F5F9",
        Accent: "#059669",
        Success: "#059669",
        Danger: "#E11D48",
        Neutral: "#64748B");

    /// <summary>Normalizes a customer palette or explains why it cannot be stored.</summary>
    public static bool TryNormalize(InvoicePdfPalette? candidate, out InvoicePdfPalette palette, out string error)
    {
        palette = Default;
        if (candidate == null)
        {
            error = "A palette is required.";
            return false;
        }

        if (!Hex(candidate.Text, out var text)
            || !Hex(candidate.SecondaryText, out var secondary)
            || !Hex(candidate.MutedText, out var muted)
            || !Hex(candidate.Border, out var border)
            || !Hex(candidate.Surface, out var surface)
            || !Hex(candidate.Accent, out var accent)
            || !Hex(candidate.Success, out var success)
            || !Hex(candidate.Danger, out var danger)
            || !Hex(candidate.Neutral, out var neutral))
        {
            error = "Each color must be a #RRGGBB value.";
            return false;
        }

        palette = new(text, secondary, muted, border, surface, accent, success, danger, neutral);
        const string paper = "#FFFFFF";
        if (Contrast(text, paper) < 4.5 || Contrast(secondary, paper) < 4.5 || Contrast(muted, paper) < 4.5)
        {
            error = "Text, secondary text, and muted text must be readable on a white page.";
            return false;
        }

        if (Contrast(text, surface) < 4.5 || Contrast(secondary, surface) < 4.5 || Contrast(muted, surface) < 3)
        {
            error = "Text must stay readable on the shaded surface.";
            return false;
        }

        if (Contrast(accent, paper) < 3 || Contrast(success, paper) < 3 || Contrast(danger, paper) < 3 || Contrast(neutral, paper) < 3)
        {
            error = "Accent and status colors must stay visible on a white page.";
            return false;
        }

        // Balance due uses accent, and amount paid uses success, both inside the shaded totals surface.
        if (Contrast(accent, surface) < 3 || Contrast(success, surface) < 3)
        {
            error = "Accent and success colors must stay readable on the shaded totals surface.";
            return false;
        }

        if (Contrast(surface, paper) > 1.45)
        {
            error = "The shaded surface must stay a light tint. The page background is white.";
            return false;
        }

        error = "";
        return true;
    }

    private static bool Hex(string? value, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (text.Length != 7 || text[0] != '#') return false;
        for (var i = 1; i < 7; i++)
        {
            if (!Uri.IsHexDigit(text[i])) return false;
        }

        normalized = "#" + text[1..].ToUpperInvariant();
        return true;
    }

    private static double Contrast(string left, string right)
    {
        var lighter = Math.Max(Luminance(left), Luminance(right));
        var darker = Math.Min(Luminance(left), Luminance(right));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(string hex)
    {
        static double Channel(int value)
        {
            var channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        var r = int.Parse(hex.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = int.Parse(hex.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = int.Parse(hex.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (0.2126 * Channel(r)) + (0.7152 * Channel(g)) + (0.0722 * Channel(b));
    }
}

/// <summary>Resolved invoice PDF appearance. Callers pass this in; the document does not load it.</summary>
public sealed record InvoicePdfAppearance(
    InvoicePdfPalette Colors,
    bool ShowAccountGoBranding = true,
    int SettingsRevision = 0)
{
    /// <summary>Footer attribution color. Customers cannot change it.</summary>
    public const string AttributionColor = "#64748B";

    /// <summary>Today's invoice PDF: default palette and visible AccountGo attribution.</summary>
    public static InvoicePdfAppearance Default { get; } = new(InvoicePdfPalette.Default);

    /// <summary>Stable hash of the colors and attribution flag.</summary>
    public string Fingerprint()
    {
        var json = JsonSerializer.Serialize(new { Colors, ShowAccountGoBranding });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>True when rendering this appearance depends on an organization customization capability.</summary>
    public bool RequiresEntitlement =>
        !ShowAccountGoBranding || Colors != InvoicePdfPalette.Default;
}

/// <summary>Appearance frozen with one email attachment, independent of later setting edits.</summary>
public sealed record InvoicePdfAppearanceStamp(string Hash, bool HideAccountGoBranding, bool RequiresEntitlement, int SettingsRevision)
{
    public static InvoicePdfAppearanceStamp From(InvoicePdfAppearance appearance) => new(
        appearance.Fingerprint(),
        !appearance.ShowAccountGoBranding,
        appearance.RequiresEntitlement,
        appearance.SettingsRevision);

    public static string Serialize(InvoicePdfAppearance appearance) =>
        JsonSerializer.Serialize(From(appearance));

    public static InvoicePdfAppearanceStamp? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<InvoicePdfAppearanceStamp>(json); }
        catch (JsonException) { return null; }
    }
}

/// <summary>A customer-supplied appearance that has not yet been resolved against entitlement.</summary>
public sealed record InvoiceBrandingDesired(InvoicePdfPalette? Palette, bool HideAccountGoBranding, int Revision);

/// <summary>Desired settings plus the appearance a PDF would use right now.</summary>
public sealed record InvoiceBrandingSettingsModel(
    int SchemaVersion,
    InvoicePdfPalette? RequestedPalette,
    bool HideAccountGoBranding,
    int Revision,
    bool Applied,
    string? InactiveReason,
    InvoicePdfPalette EffectivePalette,
    bool EffectiveShowsAccountGoBranding);

/// <summary>What the current organization may do with invoice PDF appearance.</summary>
public sealed record InvoiceBrandingCapabilities(
    int CompanyId,
    bool CanCustomizeInvoiceAppearance,
    bool CanHideAccountGoBranding,
    bool CanManageSettings,
    bool UpgradeAvailable,
    string? UnavailableReason,
    bool HasRetainedSettings,
    bool EffectiveShowsAccountGoBranding,
    string? InactiveReason);

/// <summary>Raised when a palette cannot be stored or previewed.</summary>
public sealed class InvoiceBrandingValidationException(string message) : ArgumentException(message);
