using Api.DocumentCaptures;
using Api.DocumentCaptures.Mapping;
using System.Text.Json;
using Api.DocumentCaptures.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class LocalOcrWorkerLimitsTests
{
    private static readonly CaptureWorkerHeader Header = new("image/png", 1, 3);
    private static readonly byte[] Input = [1, 2, 3];

    [Theory]
    [InlineData(2, 422, "DOCUMENT_UNREADABLE")]
    [InlineData(3, 503, "DOCUMENT_PROVIDER_UNAVAILABLE")]
    [InlineData(137, 503, "DOCUMENT_PROVIDER_UNAVAILABLE")]
    [InlineData(-11, 503, "DOCUMENT_PROVIDER_UNAVAILABLE")]
    [InlineData(9, 503, "DOCUMENT_PROVIDER_UNAVAILABLE")]
    public async Task ExitCodeDistinguishesBadInputFromWorkerFailure(int code, int status, string expected)
    {
        var worker = new FakeWorker([]) { ExitCode = code };
        worker.Complete();
        var error = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => RunAsync(worker));
        Assert.Equal((status, expected), (error.StatusCode, error.ErrorCode));
        Assert.False(worker.Killed);
    }

    [Fact]
    public async Task OutputLimitKillsWorkerAndReturnsTooComplex()
    {
        var worker = new FakeWorker(new byte[CaptureWorkerProtocol.MaxOutputBytes + 1]);
        var error = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => RunAsync(worker));
        Assert.Equal((422, "DOCUMENT_TOO_COMPLEX"), (error.StatusCode, error.ErrorCode));
        Assert.True(worker.Killed);
    }

    [Fact]
    public async Task MemoryLimitKillsWorkerAndReturnsTooComplex()
    {
        var worker = new FakeWorker([]) { MemoryBytes = 1024 * 1024 + 1 };
        var error = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => RunAsync(worker));
        Assert.Equal((422, "DOCUMENT_TOO_COMPLEX"), (error.StatusCode, error.ErrorCode));
        Assert.True(worker.Killed);
    }

    [Fact]
    public async Task DeadlineKillsWorkerAndReturnsRetryableBusy()
    {
        var worker = new FakeWorker([]);
        var clock = new DeadlineClock();
        var result = RunAsync(worker, clock);
        clock.Expire();
        var error = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => result);
        Assert.Equal((503, "DOCUMENT_BUSY"), (error.StatusCode, error.ErrorCode));
        Assert.True(worker.Killed);
    }

    [Fact]
    public async Task CancellationKillsWorkerAndPropagates()
    {
        var worker = new FakeWorker([]);
        using var cancel = new CancellationTokenSource();
        var result = RunAsync(worker, token: cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result);
        Assert.True(worker.Killed);
    }

    [Fact]
    public async Task QueueDeadlineRejectsWithoutStartingTheWorker()
    {
        var clock = new DeadlineClock(LocalOcrDocumentFieldExtractor.GateWait);
        using var extractor = new LocalOcrDocumentFieldExtractor(Options.Create(new DocumentCaptureOptions()), clock,
            new MetricsLogger(), _ => throw new InvalidOperationException("No worker should start."));
        using var held = await extractor.AdmitAsync(1, CancellationToken.None);
        var waiting = extractor.AdmitAsync(1, CancellationToken.None);
        Assert.Equal(1, extractor.WaitingCount);
        clock.Expire();
        var error = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => waiting);
        Assert.Equal((429, "DOCUMENT_DECODE_BUSY"), (error.StatusCode, error.ErrorCode));
        Assert.Equal(0, extractor.WaitingCount);
    }

    [Theory]
    [InlineData("/custom/dotnet", "/custom/dotnet")]
    [InlineData("/opt/bin/Api", "dotnet")]
    [InlineData(null, "dotnet")]
    public void StartInfoUsesTheCurrentDotnetHostAndOnlyPassesRuntimeRoots(string? host, string expected)
    {
        var environment = new Dictionary<string, string>
        {
            ["DOTNET_ROOT"] = "/custom/runtime",
            ["DOTNET_ROOT_ARM64"] = "/custom/arm64",
            ["DOTNET_HOST_PATH"] = "/unexpected/dotnet",
            ["SECRET_KEY"] = "sensitive"
        };
        var start = LocalOcrDocumentFieldExtractor.BuildStartInfo("/test assets/Api.CaptureWorker.dll",
            new DocumentCaptureOcrWorkerOptions(), host, key => environment.GetValueOrDefault(key));

        Assert.False(start.UseShellExecute);
        Assert.Equal(expected, start.FileName);
        Assert.Equal("/test assets/Api.CaptureWorker.dll", Assert.Single(start.ArgumentList));
        Assert.Equal("/custom/runtime", start.Environment["DOTNET_ROOT"]);
        Assert.Equal("/custom/arm64", start.Environment["DOTNET_ROOT_ARM64"]);
        Assert.DoesNotContain("SECRET_KEY", start.Environment.Keys);
        Assert.DoesNotContain("DOTNET_HOST_PATH", start.Environment.Keys);
    }

    [Fact]
    public async Task ExtractionLogContainsOnlyOutcomeAndMetrics()
    {
        const string sensitive = "PRIVATE_WORKER_DOCUMENT_TEXT";
        var json = JsonSerializer.SerializeToUtf8Bytes(
            new CaptureWorkerResult([new TextLine(1, sensitive, 0, 0, 1, 1, TextLineSource.Ocr)], [1], [], []),
            CaptureWorkerProtocol.Json);
        var worker = new FakeWorker(json) { MemoryBytes = 123456 };
        var logger = new MetricsLogger();
        using var extractor = new LocalOcrDocumentFieldExtractor(Options.Create(new DocumentCaptureOptions()),
            TimeProvider.System, logger, _ => worker);
        var resultTask = extractor.ExtractAsync(new DocumentExtractionRequest("purchase-invoice", 1, "image/png", Input, 1),
            CancellationToken.None);
        worker.Complete();
        var result = await resultTask;

        Assert.Contains(sensitive, result.RawText);
        var log = Assert.Single(logger.Entries);
        Assert.DoesNotContain(sensitive, log.Text);
        Assert.Null(log.Exception);
        Assert.Equal("ok", log.State["Outcome"]);
        Assert.Equal(0, log.State["ExitCode"]);
        Assert.Equal(123456L, log.State["PeakRssBytes"]);
        Assert.Equal(1, log.State["PageCount"]);
        Assert.Equal(1, log.State["OcrPageCount"]);
        Assert.Equal(1, log.State["LineCount"]);
        Assert.True((long)log.State["QueueMs"]! >= 0);
        Assert.True((long)log.State["RunMs"]! >= 0);
    }

    [Fact]
    public async Task ExtractionReturnsModelAndRulesProviderVersionWithinLimit()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(
            new CaptureWorkerResult([new TextLine(1, "Acme Ltd", 0, 0, 100, 20, TextLineSource.Ocr)], [1], [], []),
            CaptureWorkerProtocol.Json);
        var worker = new FakeWorker(json);
        using var extractor = new LocalOcrDocumentFieldExtractor(Options.Create(new DocumentCaptureOptions()),
            TimeProvider.System, new MetricsLogger(), _ => worker);
        var resultTask = extractor.ExtractAsync(new DocumentExtractionRequest("purchase-invoice", 1, "image/png", Input, 1),
            CancellationToken.None);
        worker.Complete();
        var result = await resultTask;

        Assert.Equal("ocr-1+d5de5df3+20b69451+rules-3", result.ProviderVersion);
        Assert.Equal(LocalOcrDocumentFieldExtractor.CurrentProviderVersion, result.ProviderVersion);
        Assert.True(result.ProviderVersion.Length <= 100);
    }

    private static readonly TextLine[] MappedDocumentLines =
    [
        new(1, "Northstar Supplies Ltd", 0, 0, 100, 20, TextLineSource.Ocr),
        new(1, "Invoice No: NS-20457", 0, 22, 100, 40, TextLineSource.Ocr),
        new(1, "Account 123456789012", 0, 42, 100, 60, TextLineSource.Ocr),
        new(1, "Date: 2026-11-12", 0, 62, 100, 80, TextLineSource.Ocr),
        new(1, "Subtotal: 100.00", 0, 82, 100, 100, TextLineSource.Ocr),
        new(1, "Tax: 7.00", 0, 102, 100, 120, TextLineSource.Ocr),
        new(1, "Total: 107.00", 0, 122, 100, 140, TextLineSource.Ocr),
    ];

    [Fact]
    public async Task ExtractionWithTheDefaultMapperStoresExactlyWhatTheRuleParserFound()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new CaptureWorkerResult(MappedDocumentLines, [1], [], []), CaptureWorkerProtocol.Json);
        var worker = new FakeWorker(json);
        using var extractor = new LocalOcrDocumentFieldExtractor(Options.Create(new DocumentCaptureOptions()),
            TimeProvider.System, new MetricsLogger(), _ => worker);
        var resultTask = extractor.ExtractAsync(new DocumentExtractionRequest("purchase-invoice", 1, "image/png", Input, 1),
            CancellationToken.None);
        worker.Complete();
        var result = await resultTask;

        var expected = DocumentTextFieldParser.Parse("purchase-invoice", MappedDocumentLines);
        Assert.Equal(expected.FieldsJson, result.FieldsJson);
        Assert.Equal(expected.Warnings, result.Warnings);
    }

    [Fact]
    public async Task ExtractionGivesTheMapperOnlyMaskedTextAndGuardsWhatItReturns()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new CaptureWorkerResult(MappedDocumentLines, [1], [], []), CaptureWorkerProtocol.Json);
        var worker = new FakeWorker(json);
        var mapper = new RecordingMapper(request =>
            // The mapper invents an invoice number and a different vendor spelling.
            request.RuleFieldsJson.Replace("NS-20457", "NS-99999", StringComparison.Ordinal)
                .Replace("Northstar Supplies Ltd", "Northstar Supplies Limited", StringComparison.Ordinal));
        using var extractor = new LocalOcrDocumentFieldExtractor(Options.Create(new DocumentCaptureOptions()),
            TimeProvider.System, new MetricsLogger(), _ => worker, mapper);
        var resultTask = extractor.ExtractAsync(new DocumentExtractionRequest("purchase-invoice", 1, "image/png", Input, 1),
            CancellationToken.None);
        worker.Complete();
        var result = await resultTask;

        var seen = Assert.Single(mapper.Requests);
        Assert.DoesNotContain("123456789012", string.Join('\n', seen.Lines.Select(l => l.Text)), StringComparison.Ordinal);
        Assert.Contains("NS-20457", string.Join('\n', seen.Lines.Select(l => l.Text)), StringComparison.Ordinal);
        using var fields = JsonDocument.Parse(result.FieldsJson);
        Assert.Equal("NS-20457", fields.RootElement.GetProperty("vendorInvoiceNo").GetString());
        Assert.Equal("Northstar Supplies Limited", fields.RootElement.GetProperty("vendorName").GetString());
        Assert.Equal("ai", fields.RootElement.GetProperty("fieldSources").GetProperty("vendorName").GetString());
        Assert.Contains("AI_VALUE_NOT_IN_SOURCE", result.Warnings);
        Assert.Contains("AI_DISAGREES_WITH_RULES", result.Warnings);
    }

    [Fact]
    public async Task ExtractionNeverCallsTheMapperForBankDocuments()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new CaptureWorkerResult(MappedDocumentLines, [1], [], []), CaptureWorkerProtocol.Json);
        var worker = new FakeWorker(json);
        var mapper = new RecordingMapper(request => request.RuleFieldsJson);
        using var extractor = new LocalOcrDocumentFieldExtractor(Options.Create(new DocumentCaptureOptions()),
            TimeProvider.System, new MetricsLogger(), _ => worker, mapper);
        var resultTask = extractor.ExtractAsync(new DocumentExtractionRequest("bank", 1, "image/png", Input, 1), CancellationToken.None);
        worker.Complete();
        await resultTask;

        Assert.Empty(mapper.Requests);
    }

    private sealed class RecordingMapper(Func<CaptureMappingRequest, string> map) : ICaptureFieldMapper
    {
        public List<CaptureMappingRequest> Requests { get; } = [];

        public Task<CaptureMappingResult> MapAsync(CaptureMappingRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new CaptureMappingResult(map(request), "recording", "1", Array.Empty<string>()));
        }
    }

    private sealed class MetricsLogger : ILogger<LocalOcrDocumentFieldExtractor>
    {
        internal List<(string Text, Exception? Exception, Dictionary<string, object?> State)> Entries { get; } = [];
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((formatter(state, exception), exception,
                ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary()));
    }

    private static Task<byte[]> RunAsync(FakeWorker worker, TimeProvider? clock = null, CancellationToken token = default) =>
        CaptureWorkerRunner.RunAsync(worker, Header, Input, 1, clock ?? TimeProvider.System, token);

    internal sealed class FakeWorker(byte[] output) : ICaptureWorkerProcess
    {
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Killed { get; private set; }
        public Stream Input { get; } = new MemoryStream();
        public Stream Output { get; } = new MemoryStream(output);
        public Stream Errors { get; } = new MemoryStream();
        public int ExitCode { get; init; }
        public void Complete() => _exit.TrySetResult();
        public bool HasExited => _exit.Task.IsCompleted;
        public long MemoryBytes { get; init; }
        public long WorkingSetBytes => MemoryBytes;
        public Task WaitForExitAsync(CancellationToken token) => _exit.Task.WaitAsync(token);
        public void KillTree()
        {
            Killed = true;
            _exit.TrySetResult();
        }
        public void Dispose()
        {
            Input.Dispose();
            Output.Dispose();
            Errors.Dispose();
        }
    }

    private sealed class DeadlineClock(TimeSpan? expected = null) : TimeProvider
    {
        private Action? _expire;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime == (expected ?? CaptureWorkerRunner.Deadline))
                _expire = () => callback(state);
            return TimeProvider.System.CreateTimer(callback, state, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public void Expire() => (_expire ?? throw new InvalidOperationException())();
    }
}
