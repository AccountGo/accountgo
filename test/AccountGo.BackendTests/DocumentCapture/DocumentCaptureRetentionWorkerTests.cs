using Api.DocumentCaptures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class DocumentCaptureRetentionWorkerTests
{
    [Fact]
    public async Task WorkerWaitsConfiguredColdStartDelayBeforeOpeningScope()
    {
        var scopes = new CountingScopeFactory();
        var time = new RecordingTimeProvider();
        var worker = new DocumentCaptureRetentionWorker(scopes, NullLogger<DocumentCaptureRetentionWorker>.Instance,
            Options.Create(new DocumentCaptureOptions { RetentionStartDelaySeconds = 60 }), time);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            for (var attempt = 0; attempt < 50 && time.LastDueTime is null && scopes.CreateCount == 0; attempt++)
                await Task.Delay(1);
            Assert.Equal(TimeSpan.FromSeconds(60), time.LastDueTime);
            Assert.Equal(0, scopes.CreateCount);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private sealed class CountingScopeFactory : IServiceScopeFactory
    {
        public int CreateCount { get; private set; }

        public IServiceScope CreateScope()
        {
            CreateCount++;
            throw new InvalidOperationException("The worker should still be in its cold-start delay.");
        }
    }

    private sealed class RecordingTimeProvider : TimeProvider
    {
        public TimeSpan? LastDueTime { get; private set; }
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
        public override long GetTimestamp() => TimeProvider.System.GetTimestamp();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            LastDueTime = dueTime;
            return TimeProvider.System.CreateTimer(callback, state, dueTime, period);
        }
    }
}
