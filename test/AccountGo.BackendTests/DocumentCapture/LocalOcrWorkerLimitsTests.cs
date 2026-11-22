using Api.DocumentCaptures;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class LocalOcrWorkerLimitsTests
{
    private static readonly CaptureWorkerHeader Header = new("image/png", 1, 3);
    private static readonly byte[] Input = [1, 2, 3];

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

    private static Task<byte[]> RunAsync(FakeWorker worker, TimeProvider? clock = null, CancellationToken token = default) =>
        CaptureWorkerRunner.RunAsync(worker, Header, Input, 1, clock ?? TimeProvider.System, token);

    private sealed class FakeWorker(byte[] output) : ICaptureWorkerProcess
    {
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Killed { get; private set; }
        public Stream Input { get; } = new MemoryStream();
        public Stream Output { get; } = new MemoryStream(output);
        public Stream Errors { get; } = new MemoryStream();
        public int ExitCode => 0;
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

    private sealed class DeadlineClock : TimeProvider
    {
        private Action? _expire;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime == CaptureWorkerRunner.Deadline)
                _expire = () => callback(state);
            return TimeProvider.System.CreateTimer(callback, state, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public void Expire() => (_expire ?? throw new InvalidOperationException())();
    }
}
