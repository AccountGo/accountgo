// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Api.DocumentCaptures.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private const string AssistantAttachmentKind = "assistant-attachment";
    internal const string CurrentProviderVersion = CaptureWorkerModels.Version + "+" + DocumentTextFieldParser.RulesVersion;
    internal static readonly TimeSpan GateWait = TimeSpan.FromSeconds(15);
    private readonly DocumentCaptureOcrWorkerOptions _options;
    private readonly string _workerPath;
    private readonly SemaphoreSlim _gate;
    private readonly TimeProvider _clock;
    private readonly ILogger<LocalOcrDocumentFieldExtractor> _logger;
    private readonly Func<ProcessStartInfo, ICaptureWorkerProcess> _startWorker;
    private int _waiting;

    public LocalOcrDocumentFieldExtractor(IOptions<DocumentCaptureOptions> options, TimeProvider? clock = null,
        ILogger<LocalOcrDocumentFieldExtractor>? logger = null)
        : this(options, clock ?? TimeProvider.System, logger ?? NullLogger<LocalOcrDocumentFieldExtractor>.Instance,
            start => new CaptureWorkerProcess(Process.Start(start) ?? throw new InvalidOperationException()))
    {
    }

    internal LocalOcrDocumentFieldExtractor(IOptions<DocumentCaptureOptions> options, TimeProvider clock,
        ILogger<LocalOcrDocumentFieldExtractor> logger, Func<ProcessStartInfo, ICaptureWorkerProcess> startWorker)
    {
        _options = options.Value.OcrWorker;
        _workerPath = _options.ResolveWorkerPath();
        _gate = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);
        _clock = clock;
        _logger = logger;
        _startWorker = startWorker;
    }

    public bool IsConfigured => true;
    public string ProviderName => "local-ocr";
    internal int WaitingCount => Volatile.Read(ref _waiting);

    internal async Task<Admission> AdmitAsync(int pageCount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var started = _clock.GetTimestamp();
        if (_gate.Wait(0))
            return new Admission(_gate, _clock.GetElapsedTime(started).TotalMilliseconds);

        try
        {
            try
            {
                if (Interlocked.Increment(ref _waiting) > 4 * _options.MaxConcurrency)
                    throw Busy();
                using var queueTimeout = new CancellationTokenSource(GateWait, _clock);
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(queueTimeout.Token, cancellationToken);
                try
                {
                    await _gate.WaitAsync(stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && queueTimeout.IsCancellationRequested)
                {
                    throw Busy();
                }

                return new Admission(_gate, _clock.GetElapsedTime(started).TotalMilliseconds);
            }
            finally
            {
                Interlocked.Decrement(ref _waiting);
            }
        }
        catch (DocumentCaptureServiceException exception)
        {
            LogOutcome(exception.ErrorCode, _clock.GetElapsedTime(started).TotalMilliseconds, 0, new(), pageCount, null);
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogOutcome("cancelled", _clock.GetElapsedTime(started).TotalMilliseconds, 0, new(), pageCount, null);
            throw;
        }
    }

    public async Task<DocumentExtractionResult> ExtractAsync(DocumentExtractionRequest request, CancellationToken cancellationToken)
    {
        using var admission = await AdmitAsync(request.PageCount, cancellationToken).ConfigureAwait(false);
        return await ExtractAdmittedAsync(request, admission, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<DocumentExtractionResult> ExtractAdmittedAsync(DocumentExtractionRequest request,
        Admission admission, CancellationToken cancellationToken)
    {
        var started = _clock.GetTimestamp();
        var metrics = new CaptureWorkerMetrics();
        CaptureWorkerResult? result = null;
        var outcome = "DOCUMENT_PROVIDER_UNAVAILABLE";

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ICaptureWorkerProcess worker;
            try
            {
                worker = _startWorker(BuildStartInfo(_workerPath, _options, Environment.ProcessPath, Environment.GetEnvironmentVariable));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                throw new DocumentCaptureServiceException(503, "DOCUMENT_PROVIDER_UNAVAILABLE");
            }

            // Assistant attachments can be any document (a web page, a guide), so the worker keeps a row's pieces apart
            // and the stored text follows columns. Financial kinds keep the row text their field rules were built on.
            var segments = request.Kind == AssistantAttachmentKind;
            var header = new CaptureWorkerHeader(request.ContentType, request.PageCount, MaxOcrPages: 3, segments);
            var payload = await CaptureWorkerRunner.RunAsync(worker, header, request.DocumentBytes,
                _options.MaxWorkerMemoryMb, _clock, cancellationToken, metrics).ConfigureAwait(false);
            result = ParseWorkerResponse(payload, request.PageCount);
            var (lines, rawText) = BoundLines(segments ? TextLayout.Rows(result.Lines) : result.Lines);
            if (segments)
                rawText = BoundLines(TextLayout.ReadingLines(result.Lines)).RawText;
            var parsed = DocumentTextFieldParser.Parse(request.Kind, lines);
            // Rule fields are returned as soon as the rules have read the document. An optional field mapper never runs here: a later
            // background job calls CaptureFieldMappingRunner, so a slow mapper can't delay a scan or hold the OCR admission slot.
            var extraction = new DocumentExtractionResult(parsed.FieldsJson, request.Kind, 1,
                result.Warnings.Concat(parsed.Warnings).Distinct(StringComparer.Ordinal).Take(32).ToArray(),
                rawText, ProviderName, CurrentProviderVersion,
                TextPageCount: rawText.Length == 0 ? 0 : Math.Max(1, lines.Select(line => line.Page).Distinct().Count()));
            outcome = "ok";
            return extraction;
        }
        catch (DocumentCaptureServiceException exception)
        {
            outcome = exception.ErrorCode;
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";
            throw;
        }
        finally
        {
            LogOutcome(outcome, admission.QueueMilliseconds, _clock.GetElapsedTime(started).TotalMilliseconds,
                metrics, request.PageCount, result);
        }
    }

    private void LogOutcome(string outcome, double queueMs, double runMs, CaptureWorkerMetrics metrics,
        int pageCount, CaptureWorkerResult? result) =>
        _logger.LogInformation("Local document OCR completed. Outcome={Outcome} ExitCode={ExitCode} QueueMs={QueueMs} RunMs={RunMs} PeakRssBytes={PeakRssBytes} Pages={PageCount} OcrPages={OcrPageCount} Lines={LineCount}",
            outcome, metrics.ExitCode, (long)Math.Ceiling(queueMs), (long)Math.Ceiling(runMs), metrics.PeakRssBytes,
            pageCount, result?.OcrPages.Length ?? 0, result?.Lines.Length ?? 0);

    internal static ProcessStartInfo BuildStartInfo(string workerPath, DocumentCaptureOcrWorkerOptions options,
        string? processPath, Func<string, string?> readEnvironment)
    {
        var start = new ProcessStartInfo
        {
            FileName = string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase)
                ? processPath! : "dotnet",
            WorkingDirectory = Path.GetDirectoryName(workerPath)!,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(workerPath);

        // The runtime root can be required by a nonstandard .NET install.
        // Do not inherit any other value from the API process.
        start.Environment.Clear();
        foreach (var name in new[] { "DOTNET_ROOT", "DOTNET_ROOT_ARM64", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM", "DOTNET_ROOT_X86", "DOTNET_ROOT(x86)" })
            if (readEnvironment(name) is { Length: > 0 } root)
                start.Environment[name] = root;

        start.Environment["ACCOUNTGO_CAPTURE_OCR_THREADS"] = options.IntraOpThreads.ToString(CultureInfo.InvariantCulture);
        start.Environment["ACCOUNTGO_CAPTURE_OCR_ARENA"] = options.EnableCpuMemoryArena ? "1" : "0";
        start.Environment["DOTNET_gcServer"] = "0";
        start.Environment["DOTNET_TieredPGO"] = "0";
        return start;
    }

    private static DocumentCaptureServiceException Busy() => new(429, "DOCUMENT_DECODE_BUSY");

    internal sealed class Admission(SemaphoreSlim gate, double queueMilliseconds) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        internal double QueueMilliseconds { get; } = queueMilliseconds;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
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
