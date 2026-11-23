// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
namespace Api.DocumentCaptures.Mapping;

/// <summary>
/// What a field mapper may read. Only <see cref="Text"/> is ever used; the image modes exist so a later change can enable them
/// behind a flag, and the request factory refuses them until then.
/// </summary>
public enum CaptureMappingInputMode
{
    Text,
    TextPlusImageOnWeakOcr,
    TextPlusImage
}

/// <summary>One line of the document's own text, with its page position. The text is masked before it gets here.</summary>
public sealed record CaptureMappingLine(int Page, string Text, float X0, float Y0, float X1, float Y1);

/// <summary>
/// The whole request a mapper sees: the rule parser's canonical output and the masked text lines. It never carries images,
/// the workspace's parties, or anything else from the workspace.
/// </summary>
public sealed record CaptureMappingRequest(
    string Kind,
    int SchemaVersion,
    string RuleFieldsJson,
    IReadOnlyList<CaptureMappingLine> Lines,
    CaptureMappingInputMode InputMode,
    string RulesVersion);

public sealed record CaptureMappingResult(
    string FieldsJson,
    string MapperName,
    string MapperVersion,
    IReadOnlyList<string> Warnings);

/// <summary>
/// The seam an optional field mapper plugs into. A mapper only suggests fields: it never saves, approves or posts anything,
/// and every value it returns goes through <see cref="CaptureMappingGuards"/> before it is stored.
/// </summary>
public interface ICaptureFieldMapper
{
    Task<CaptureMappingResult> MapAsync(CaptureMappingRequest request, CancellationToken cancellationToken);
}

/// <summary>The default mapper: returns the rule fields unchanged, so the stored fields are byte-identical to the rule parser's.</summary>
public sealed class RuleOnlyFieldMapper : ICaptureFieldMapper
{
    public const string Name = "rules";
    public const string Version = CaptureFieldMapping.ContractVersion;

    public Task<CaptureMappingResult> MapAsync(CaptureMappingRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new CaptureMappingResult(request.RuleFieldsJson, Name, Version, Array.Empty<string>()));
}

public static class CaptureFieldMapping
{
    /// <summary>The version of the request/result contract between the extractor and a mapper. Recorded in the rules register.</summary>
    public const string ContractVersion = "mapper-contract-1";

    /// <summary>Hard cut-off for one mapping call: a slower mapper is abandoned and the rule fields stay.</summary>
    public static readonly TimeSpan HardTimeout = TimeSpan.FromSeconds(6);
}
