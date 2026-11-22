// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace Api.DocumentCaptures;

public sealed class DocumentCaptureOptions
{
    public const string SectionName = "DocumentCapture";
    public bool Enabled { get; set; }
    public string Extractor { get; set; } = "None";
    public int ExtractionLeaseSeconds { get; set; } = 60;
    public int MaxAttemptsPerHour { get; set; } = 20;
    public int MaxAttemptsPerDay { get; set; } = 60;
    public int ImageDecodeSlots { get; set; } = DocumentCaptureImageLimits.DecodeGateSlots;
    public int PdfParseSlots { get; set; } = DocumentCapturePdfLimits.ParseGateSlots;
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
            || (!string.Equals(options.Extractor, "None", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(options.Extractor, "Fake", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Document capture limits are outside the supported range.");

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
