// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Api.DocumentCaptures.Parsing;
using Microsoft.Extensions.Options;

namespace Api.DocumentCaptures;

public sealed class DocumentCaptureOcrWorkerOptions
{
    public string? WorkerPath { get; set; }
    public int MaxConcurrency { get; set; } = 1;
    public int MaxWorkerMemoryMb { get; set; } = 1024;
    public int IntraOpThreads { get; set; } = 2;
    public bool EnableCpuMemoryArena { get; set; } = true;

    internal string ResolveWorkerPath() => string.IsNullOrWhiteSpace(WorkerPath)
        ? Path.Combine(AppContext.BaseDirectory, "capture-worker", "Api.CaptureWorker.dll")
        : Path.GetFullPath(WorkerPath);

    internal void ValidateFiles()
    {
        var worker = ResolveWorkerPath();
        if (!File.Exists(worker))
            throw new InvalidOperationException("Local OCR worker is missing. Publish Api.CaptureWorker and set DocumentCapture:Extractor:WorkerPath.");
        var models = Path.Combine(Path.GetDirectoryName(worker)!, "Models");
        if (!File.Exists(Path.Combine(models, "SHA256SUMS"))
            || CaptureWorkerModels.Files.Any(model => !File.Exists(Path.Combine(models, model.File))))
            throw new InvalidOperationException("Local OCR models or SHA256SUMS are missing from the worker's Models directory.");
    }
}

public sealed class LocalOcrDocumentFieldExtractor : IDocumentFieldExtractor, IDisposable
{
    private static readonly TimeSpan GateWait = TimeSpan.FromMilliseconds(500);
    private readonly DocumentCaptureOcrWorkerOptions _options;
    private readonly string _workerPath;
    private readonly SemaphoreSlim _gate;
    private readonly TimeProvider _clock;

    public LocalOcrDocumentFieldExtractor(IOptions<DocumentCaptureOptions> options, TimeProvider? clock = null)
    {
        _options = options.Value.OcrWorker;
        _workerPath = _options.ResolveWorkerPath();
        _gate = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);
        _clock = clock ?? TimeProvider.System;
    }

    public bool IsConfigured => true;
    public string ProviderName => "local-ocr";

    public async Task<DocumentExtractionResult> ExtractAsync(DocumentExtractionRequest request, CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(GateWait, cancellationToken).ConfigureAwait(false))
            throw new DocumentCaptureServiceException(429, "DOCUMENT_DECODE_BUSY");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = Path.GetDirectoryName(_workerPath)!,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(_workerPath);

            // Keep application configuration and credentials out of the parser process.
            start.Environment.Clear();
            start.Environment["ACCOUNTGO_CAPTURE_OCR_THREADS"] = _options.IntraOpThreads.ToString(CultureInfo.InvariantCulture);
            start.Environment["ACCOUNTGO_CAPTURE_OCR_ARENA"] = _options.EnableCpuMemoryArena ? "1" : "0";
            start.Environment["DOTNET_gcServer"] = "0";
            start.Environment["DOTNET_TieredPGO"] = "0";
            var process = Process.Start(start) ?? throw new InvalidOperationException("Local OCR worker could not start.");
            var header = new CaptureWorkerHeader(request.ContentType, request.PageCount, MaxOcrPages: 3);
            var payload = await CaptureWorkerRunner.RunAsync(new CaptureWorkerProcess(process), header, request.DocumentBytes,
                _options.MaxWorkerMemoryMb, _clock, cancellationToken).ConfigureAwait(false);
            var result = ParseWorkerResponse(payload, request.PageCount);
            var (lines, rawText) = BoundLines(result.Lines);
            var parsed = DocumentTextFieldParser.Parse(request.Kind, lines);
            return new DocumentExtractionResult(parsed.FieldsJson, request.Kind, 1,
                result.Warnings.Concat(parsed.Warnings).Distinct(StringComparer.Ordinal).Take(32).ToArray(),
                rawText, ProviderName, CaptureWorkerModels.Version);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static CaptureWorkerResult ParseWorkerResponse(byte[] payload, int pageCount)
    {
        CaptureWorkerResult? result;
        try
        {
            result = JsonSerializer.Deserialize<CaptureWorkerResult>(payload, CaptureWorkerProtocol.Json);
        }
        catch (JsonException)
        {
            // JSON exceptions can contain content. Do not propagate them across this boundary.
            throw InvalidOutput();
        }

        if (result is null || result.Lines is null || result.OcrPages is null || result.SkippedPages is null
            || result.Warnings is null || result.OcrPages.Length > 3 || result.SkippedPages.Length > pageCount
            || result.Warnings.Length > 1
            || result.Warnings.Any(warning => warning != "OCR_PARTIAL_PAGES")
            || (result.SkippedPages.Length > 0) != (result.Warnings.Length > 0))
            throw InvalidOutput();

        var pageLists = result.OcrPages.Concat(result.SkippedPages).ToArray();
        if (pageLists.Any(page => page < 1 || page > pageCount) || pageLists.Distinct().Count() != pageLists.Length)
            throw InvalidOutput();

        var priorPage = 0;
        foreach (var line in result.Lines)
        {
            if (line is null || line.Text is null || line.Page < 1 || line.Page > pageCount || line.Page < priorPage
                || !Enum.IsDefined(line.Source) || !ValidBounds(line)
                || result.SkippedPages.Contains(line.Page)
                || (line.Source == TextLineSource.Ocr) != result.OcrPages.Contains(line.Page))
                throw InvalidOutput();
            priorPage = line.Page;
        }
        return result;
    }

    private static bool ValidBounds(TextLine line) =>
        float.IsFinite(line.X0) && float.IsFinite(line.Y0) && float.IsFinite(line.X1) && float.IsFinite(line.Y1)
        && line.X0 >= 0 && line.Y0 >= 0 && line.X1 >= line.X0 && line.Y1 >= line.Y0;

    private static (IReadOnlyList<TextLine> Lines, string RawText) BoundLines(IReadOnlyList<TextLine> lines)
    {
        var result = new List<TextLine>();
        var raw = new StringBuilder();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text))
                continue;
            var remaining = DocumentCaptureFieldSchemas.MaxRawTextLength - raw.Length - (raw.Length == 0 ? 0 : 1);
            if (remaining <= 0)
                break;
            var length = Math.Min(remaining, line.Text.Length);
            if (char.IsHighSurrogate(line.Text[length - 1]))
                length--;
            if (length == 0)
                break;
            var text = line.Text[..length];
            if (raw.Length > 0)
                raw.Append('\n');
            raw.Append(text);
            result.Add(line with { Text = text });
        }
        return (result, raw.ToString());
    }

    private static DocumentCaptureServiceException InvalidOutput() => new(422, "DOCUMENT_PROVIDER_OUTPUT_INVALID");

    public void Dispose() => _gate.Dispose();
}
