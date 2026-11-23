// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.DocumentCaptures.Parsing;

namespace Api.DocumentCaptures.Mapping;

/// <summary>A document, kind or mode that must never reach a field mapper. The reason is a stable code, never document content.</summary>
public sealed class CaptureMappingRefusedException(string reason) : InvalidOperationException(reason)
{
    public string Reason { get; } = reason;
}

/// <summary>Builds the one request a mapper may see, applying the data rules before anything can leave the process.</summary>
public static class CaptureMappingRequestFactory
{
    public const string RefusedKind = "MAPPING_KIND_NOT_ALLOWED";
    public const string RefusedInputMode = "MAPPING_INPUT_MODE_NOT_ENABLED";

    /// <summary>Bank-detail and assistant-attachment captures are never mapped; only financial documents are.</summary>
    public static bool IsMappableKind(string kind) => kind is "expense-receipt" or "purchase-invoice" or "sales-invoice";

    /// <summary>
    /// Refuses bank and assistant-attachment captures and every input mode but <see cref="CaptureMappingInputMode.Text"/>, and masks
    /// account-like numbers in every line with <see cref="DocumentCaptureMasking.MaskSensitiveValue"/>. The workspace's party list is
    /// not an input here, so it cannot be sent.
    /// </summary>
    public static CaptureMappingRequest Create(
        string kind,
        int schemaVersion,
        string ruleFieldsJson,
        IReadOnlyList<TextLine> lines,
        string rulesVersion,
        CaptureMappingInputMode inputMode = CaptureMappingInputMode.Text)
    {
        if (!IsMappableKind(kind))
            throw new CaptureMappingRefusedException(RefusedKind);
        if (inputMode != CaptureMappingInputMode.Text)
            throw new CaptureMappingRefusedException(RefusedInputMode);

        var masked = lines
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .Select(line => new CaptureMappingLine(line.Page, DocumentCaptureMasking.MaskSensitiveValue(line.Text),
                line.X0, line.Y0, line.X1, line.Y1))
            .ToArray();
        return new CaptureMappingRequest(kind, schemaVersion, ruleFieldsJson, masked, inputMode, rulesVersion);
    }
}
