// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace Api.DocumentCaptures;

/// <summary>
/// Optional AI field mapping. Off by default: it runs only when <see cref="Enabled"/> is set here AND the workspace policy allows it,
/// and it never blocks a scan (the rule fields are returned first and the mapper runs in the background).
/// </summary>
public sealed class DocumentCaptureAiMappingOptions
{
    public const int MaxTimeoutSeconds = 6;
    public bool Enabled { get; set; }
    /// <summary>"anter" (the capture-mapper agent) or "fake" (development and tests only).</summary>
    public string Mapper { get; set; } = "anter";
    public string AgentSlug { get; set; } = "accountgo-capture-mapper";
    public int TimeoutSeconds { get; set; } = MaxTimeoutSeconds;
    public int MaxLines { get; set; } = 200;
    /// <summary>A per-instance safety cap; jobs over it stay queued.</summary>
    public int MaxMappingsPerMinute { get; set; } = 30;
    /// <summary>With the fake mapper only (development and tests): report the setting as unavailable for this reason, to exercise the hidden state.</summary>
    public string? FakeUnavailableReason { get; set; }

    internal void Validate(IHostEnvironment environment)
    {
        if (TimeoutSeconds is < 1 or > MaxTimeoutSeconds
            || MaxLines is < 1 or > 1_000
            || MaxMappingsPerMinute is < 1 or > 600
            || string.IsNullOrWhiteSpace(AgentSlug) || AgentSlug.Length > 100)
            throw new InvalidOperationException("Document capture AI mapping limits are outside the supported range.");
        if (!string.Equals(Mapper, "anter", StringComparison.OrdinalIgnoreCase) && !string.Equals(Mapper, "fake", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DocumentCapture:AiMapping:Mapper must be anter or fake.");
        if (string.Equals(Mapper, "fake", StringComparison.OrdinalIgnoreCase)
            && !environment.IsDevelopment()
            && !string.Equals(environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The fake field mapper is allowed only in development and tests.");
    }
}

public sealed class DocumentCaptureOptions
{
    public const string SectionName = "DocumentCapture";
    public bool Enabled { get; set; }
    public bool ExtractionHost { get; set; } = true;
    public string Extractor { get; set; } = string.Empty;
    public DocumentCaptureOcrWorkerOptions OcrWorker { get; set; } = new();
    public int ExtractionLeaseSeconds { get; set; } = 60;
    public int MaxAttemptsPerHour { get; set; } = 20;
    public int MaxAttemptsPerDay { get; set; } = 60;
    public int ImageDecodeSlots { get; set; } = DocumentCaptureImageLimits.DecodeGateSlots;
    public int PdfParseSlots { get; set; } = DocumentCapturePdfLimits.ParseGateSlots;
    public int RetentionStartDelaySeconds { get; set; } = 60;
    public int AbandonedRetentionHours { get; set; } = 24;
    public DocumentCaptureAiMappingOptions AiMapping { get; set; } = new();
    public Dictionary<string, string> FakeFixtures { get; set; } = new(StringComparer.Ordinal);

    public static void Validate(DocumentCaptureOptions options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        if (options.ExtractionLeaseSeconds is < 30 or > 300
            || options.MaxAttemptsPerHour is < 1 or > 20
            || options.MaxAttemptsPerDay is < 1 or > 60
            || options.ImageDecodeSlots is < 1 or > DocumentCaptureImageLimits.DecodeGateSlots
            || options.PdfParseSlots is < 1 or > DocumentCapturePdfLimits.MaxParseGateSlots
            || options.RetentionStartDelaySeconds is < 0 or > 3_600
            || options.AbandonedRetentionHours is < 1 or > 168)
            throw new InvalidOperationException("Document capture limits are outside the supported range.");

        options.AiMapping.Validate(environment);

        if (!string.Equals(options.Extractor, "", StringComparison.Ordinal)
            && !string.Equals(options.Extractor, "fake", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.Extractor, "local-ocr", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DocumentCapture:Extractor must be empty, fake, or local-ocr.");

        if (string.Equals(options.Extractor, "local-ocr", StringComparison.OrdinalIgnoreCase))
        {
            if (options.OcrWorker.MaxConcurrency is < 1 or > 4
                || options.OcrWorker.MaxWorkerMemoryMb is < 1 or > 1024
                || options.OcrWorker.IntraOpThreads is < 1 or > 4)
                throw new InvalidOperationException("Local OCR worker limits are outside the supported range.");
            options.OcrWorker.ValidateFiles();
        }

        if (string.Equals(options.Extractor, "Fake", StringComparison.OrdinalIgnoreCase)
            && !environment.IsDevelopment()
            && !string.Equals(environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The fake document extractor is allowed only in development and tests.");

        foreach (var fixture in options.FakeFixtures)
        {
            if (!DocumentCaptureFieldSchemas.IsSupportedKind(fixture.Key)
                || fixture.Value.Length > DocumentCaptureFieldSchemas.MaxProviderJsonLength)
                throw new InvalidOperationException("A fake document fixture has an unsupported kind or exceeds the output limit.");
            using var parsed = JsonDocument.Parse(fixture.Value);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Fake document fixtures must be JSON objects.");
        }
    }
}
