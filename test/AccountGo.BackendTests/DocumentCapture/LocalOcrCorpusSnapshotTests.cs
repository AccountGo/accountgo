// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using Api.DocumentCaptures;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace AccountGo.BackendTests.DocumentCapture;

/// <summary>
/// Opt-in corpus snapshot for layout changes: writes, for every synthetic fixture, the parser input
/// (RawText, which is the joined parser lines for financial kinds) and the parsed fields to
/// <c>ACCOUNTGO_TEST_OCR_SNAPSHOT</c>. Run it before and after a worker or parser change and diff the files.
/// </summary>
[Collection(DocumentCaptureResourceCollection.Name)]
[Trait("Category", "LocalOcr")]
public sealed class LocalOcrCorpusSnapshotTests(ITestOutputHelper output)
{
    [SnapshotFact]
    public async Task WriteCorpusSnapshot()
    {
        var options = new DocumentCaptureOptions { Extractor = "local-ocr" };
        options.OcrWorker.WorkerPath = Environment.GetEnvironmentVariable("ACCOUNTGO_TEST_OCR_WORKER");
        options.OcrWorker.ValidateFiles();
        using var extractor = new LocalOcrDocumentFieldExtractor(Options.Create(options));

        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fixtures = Path.Combine(root, "test", "AccountGo.BackendTests", "Fixtures", "DocumentCapture");
        var files = Directory.GetFiles(Path.Combine(fixtures, "Synthetic"))
            .Concat(Directory.GetFiles(Path.Combine(fixtures, "Layout")))
            .Where(file => file.EndsWith(".pdf", StringComparison.Ordinal) || file.EndsWith(".png", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var records = new List<SnapshotRecord>();
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var bytes = await File.ReadAllBytesAsync(file);
            var contentType = "application/pdf";
            var pageCount = 1;
            if (name.EndsWith(".pdf", StringComparison.Ordinal))
            {
                using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);
                pageCount = pdf.NumberOfPages;
            }
            if (name.EndsWith(".png", StringComparison.Ordinal))
            {
                var normalized = await DocumentCaptureImageNormalizer.NormalizeAsync(bytes, "image/png");
                bytes = normalized.Bytes;
                contentType = normalized.ContentType;
            }

            var isLayout = Path.GetFileName(Path.GetDirectoryName(file)) == "Layout";
            string[] kinds = isLayout || name.EndsWith(".pdf", StringComparison.Ordinal) || name.EndsWith("_clean.png", StringComparison.Ordinal)
                ? ["purchase-invoice", "assistant-attachment"]
                : ["purchase-invoice"];
            foreach (var kind in kinds)
            {
                var result = await extractor.ExtractAsync(new DocumentExtractionRequest(kind, 1, contentType, bytes, pageCount),
                    CancellationToken.None);
                var rawText = result.RawText ?? "";
                var block = kind == "assistant-attachment"
                    ? Api.Service.Ai.AssistantAttachmentMessageBuilder.BuildDelimitedUntrustedBlock(
                        "doc_snapshot", result.FieldsJson, maskedRawText: DocumentCaptureMasking.MaskRawText(rawText))
                    : null;
                records.Add(new SnapshotRecord(name, kind, result.FieldsJson, rawText, block));
            }
        }

        var path = Environment.GetEnvironmentVariable("ACCOUNTGO_TEST_OCR_SNAPSHOT")!;
        await File.WriteAllLinesAsync(path, records.Select(record => JsonSerializer.Serialize(record)));
        output.WriteLine($"Wrote {records.Count} records to {path}.");
    }

    private sealed record SnapshotRecord(string File, string Kind, string Fields, string RawText, string? Block);

    private sealed class SnapshotFactAttribute : FactAttribute
    {
        public SnapshotFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACCOUNTGO_TEST_OCR_WORKER"))
                || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACCOUNTGO_TEST_OCR_SNAPSHOT")))
                Skip = "Set ACCOUNTGO_TEST_OCR_WORKER and ACCOUNTGO_TEST_OCR_SNAPSHOT to write a corpus snapshot.";
        }
    }
}
