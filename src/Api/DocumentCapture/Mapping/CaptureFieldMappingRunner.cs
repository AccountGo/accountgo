// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Text.Json;
using Api.DocumentCaptures.Parsing;

namespace Api.DocumentCaptures.Mapping;

/// <summary>The fields to store, plus anything the review step should show about how the mapper was used.</summary>
public sealed record CaptureMappingOutcome(
    string FieldsJson,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> RuleValueHints,
    string MapperName,
    string MapperVersion);

/// <summary>Calls the registered mapper and then the guards (rules G-AI-01 to G-AI-05). It never lets a mapper failure fail the capture.</summary>
public static class CaptureFieldMappingRunner
{
    public static async Task<CaptureMappingOutcome> RunAsync(
        ICaptureFieldMapper mapper,
        string kind,
        int schemaVersion,
        string ruleFieldsJson,
        IReadOnlyList<TextLine> lines,
        string rulesVersion,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        // Documents and kinds that must never be mapped are left exactly as the rules found them.
        if (!CaptureMappingRequestFactory.IsMappableKind(kind))
            return Unchanged(ruleFieldsJson);

        var request = CaptureMappingRequestFactory.Create(kind, schemaVersion, ruleFieldsJson, lines, rulesVersion);
        try
        {
            using var cutoff = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cutoff.CancelAfter(timeout ?? CaptureFieldMapping.HardTimeout);
            var result = await mapper.MapAsync(request, cutoff.Token).ConfigureAwait(false);
            if (result is null
                || string.IsNullOrEmpty(result.FieldsJson)
                || result.FieldsJson.Length > DocumentCaptureFieldSchemas.MaxProviderJsonLength
                || string.IsNullOrWhiteSpace(result.MapperName)
                || string.IsNullOrWhiteSpace(result.MapperVersion))
                return Unavailable(ruleFieldsJson);

            var guarded = CaptureMappingGuards.Apply(kind, ruleFieldsJson, result.FieldsJson, request.Lines.Select(line => line.Text).ToArray());
            return new CaptureMappingOutcome(guarded.FieldsJson, guarded.Warnings, guarded.RuleValueHints, result.MapperName, result.MapperVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // G-AI-05: a mapper exception or timeout returns the rule fields unchanged. The exception text is never kept
            // because it could carry document content.
            return Unavailable(ruleFieldsJson);
        }
    }

    private static CaptureMappingOutcome Unchanged(string ruleFieldsJson) =>
        new(ruleFieldsJson, Array.Empty<string>(), new Dictionary<string, string>(), RuleOnlyFieldMapper.Name, RuleOnlyFieldMapper.Version);

    private static CaptureMappingOutcome Unavailable(string ruleFieldsJson) =>
        new(ruleFieldsJson, [CaptureMappingGuards.WarningUnavailable], new Dictionary<string, string>(), RuleOnlyFieldMapper.Name, RuleOnlyFieldMapper.Version);
}
