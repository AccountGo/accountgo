using Api.DocumentCaptures;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

[Collection(DocumentCaptureResourceCollection.Name)]
public sealed class DocumentCapturePdfWarmupTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task WarmupRunsOnceAfterStartupAndLogsFailureWithoutContent(bool throws, bool rejects)
    {
        using var startup = new CancellationTokenSource();
        var lifetime = new Mock<IHostApplicationLifetime>();
        lifetime.SetupGet(host => host.ApplicationStarted).Returns(startup.Token);
        var logger = new WarmupLogger();
        var calls = 0;
        var sampleIsValid = false;
        using var worker = new DocumentCapturePdfWarmup(lifetime.Object,
            Options.Create(new DocumentCaptureOptions { Enabled = true, PdfParseSlots = 2 }), logger,
            async (slots, token) =>
            {
                Interlocked.Increment(ref calls);
                var result = await DocumentCapturePdfWarmup.WarmParserAsync(slots, token);
                sampleIsValid = result.IsValid && result.PageCount == 1;
                if (throws)
                    throw new InvalidOperationException("private document content");
                return rejects ? new(false, null, "private document content") : result;
            });

        await worker.StartAsync(CancellationToken.None);
        Assert.Equal(0, Volatile.Read(ref calls));

        startup.Cancel();
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(30));
        startup.Cancel();
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.True(sampleIsValid);
        if (throws || rejects)
        {
            var warning = Assert.Single(logger.Messages);
            Assert.Equal(LogLevel.Warning, warning.Level);
            Assert.Null(warning.Exception);
            Assert.DoesNotContain("private document content", warning.Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.Empty(logger.Messages);
        }
    }

    [Fact]
    public async Task WarmupDoesNotRunWhenExtractionHostIsFalse()
    {
        using var startup = new CancellationTokenSource();
        var lifetime = new Mock<IHostApplicationLifetime>();
        lifetime.SetupGet(host => host.ApplicationStarted).Returns(startup.Token);
        var logger = new WarmupLogger();
        var calls = 0;
        using var worker = new DocumentCapturePdfWarmup(lifetime.Object,
            Options.Create(new DocumentCaptureOptions { Enabled = true, ExtractionHost = false, PdfParseSlots = 1 }), logger,
            (_, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(new DocumentCapturePdfValidationResult(true, 1, null));
            });

        await worker.StartAsync(CancellationToken.None);
        startup.Cancel();
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, Volatile.Read(ref calls));
        Assert.Empty(logger.Messages);
    }

    [Fact]
    public void OptionsValidate_AllowsExtractionHostFalseEvenWhenCaptureDisabled()
    {
        var dev = new Mock<IHostEnvironment>();
        dev.SetupGet(e => e.EnvironmentName).Returns("Development");

        // Kill switch: Enabled=false with ExtractionHost=false (forced on V1 by pool var) must never block startup.
        DocumentCaptureOptions.Validate(
            new DocumentCaptureOptions { Enabled = false, ExtractionHost = false, Extractor = "" }, dev.Object);

        DocumentCaptureOptions.Validate(
            new DocumentCaptureOptions { Enabled = false, ExtractionHost = false, Extractor = "fake" }, dev.Object);

        DocumentCaptureOptions.Validate(
            new DocumentCaptureOptions { Enabled = true, ExtractionHost = false, Extractor = "fake" }, dev.Object);
    }

    private sealed class WarmupLogger : ILogger<DocumentCapturePdfWarmup>
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add((logLevel, exception, formatter(state, exception)));
    }
}
