// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Api.DocumentCaptures;

public sealed record DocumentExtractionRequest(
    string Kind,
    int SchemaVersion,
    string ContentType,
    ReadOnlyMemory<byte> DocumentBytes,
    int PageCount);

public sealed record DocumentExtractionResult(
    string FieldsJson,
    string? DetectedKind,
    int? DetectedDocumentCount,
    IReadOnlyList<string> Warnings,
    string? RawText,
    string ProviderName,
    string ProviderVersion,
    int TextPageCount = 0);

public interface IDocumentFieldExtractor
{
    bool IsConfigured { get; }
    string? ProviderName { get; }
    Task<DocumentExtractionResult> ExtractAsync(DocumentExtractionRequest request, CancellationToken cancellationToken);
}

/// <summary>Deterministic fixture extractor. It must only be registered outside production.</summary>
public sealed class FakeDocumentFieldExtractor : IDocumentFieldExtractor
{
    private readonly IReadOnlyDictionary<string, string> _fixtures;

    public FakeDocumentFieldExtractor(IOptions<DocumentCaptureOptions> options)
    {
        _fixtures = options.Value.FakeFixtures;
        foreach (var (kind, json) in _fixtures)
        {
            using var document = JsonDocument.Parse(json);
            if (!DocumentCaptureFieldSchemas.TryValidate(kind, DocumentCaptureFieldSchemas.CurrentSchemaVersion,
                    document.RootElement, out _, out var error))
                throw new InvalidOperationException($"Fake fixture for {kind} failed schema validation: {error}.");
        }
    }

    public bool IsConfigured => true;
    public string ProviderName => "fake";

    public Task<DocumentExtractionResult> ExtractAsync(DocumentExtractionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fields = _fixtures.TryGetValue(request.Kind, out var configured)
            ? configured
            : request.Kind == "assistant-attachment" ? "{\"fields\":[]}" : "{}";
        return Task.FromResult(new DocumentExtractionResult(fields, request.Kind, 1, Array.Empty<string>(), null, "fake", "1"));
    }
}

public sealed class UnconfiguredDocumentFieldExtractor : IDocumentFieldExtractor
{
    public bool IsConfigured => false;
    public string? ProviderName => null;

    public Task<DocumentExtractionResult> ExtractAsync(DocumentExtractionRequest request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("No document field extractor is configured.");
}
