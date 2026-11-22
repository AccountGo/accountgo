// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Diagnostics;

namespace Api.DocumentCaptures;

internal interface ICaptureWorkerProcess : IDisposable
{
    Stream Input { get; }
    Stream Output { get; }
    Stream Errors { get; }
    int ExitCode { get; }
    bool HasExited { get; }
    long WorkingSetBytes { get; }
    Task WaitForExitAsync(CancellationToken token);
    void KillTree();
}

internal sealed class CaptureWorkerProcess(Process process) : ICaptureWorkerProcess
{
    public Stream Input => process.StandardInput.BaseStream;
    public Stream Output => process.StandardOutput.BaseStream;
    public Stream Errors => process.StandardError.BaseStream;
    public int ExitCode => process.ExitCode;
    public bool HasExited => process.HasExited;

    public long WorkingSetBytes
    {
        get
        {
            process.Refresh();
            try
            {
                return process.WorkingSet64;
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                return 0;
            }
        }
    }

    public Task WaitForExitAsync(CancellationToken token) => process.WaitForExitAsync(token);

    public void KillTree()
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
        }
    }

    public void Dispose() => process.Dispose();
}

internal static class CaptureWorkerRunner
{
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan MemorySampleInterval = TimeSpan.FromMilliseconds(100);

    internal static async Task<byte[]> RunAsync(ICaptureWorkerProcess worker, CaptureWorkerHeader header, ReadOnlyMemory<byte> bytes,
        int maxMemoryMb, TimeProvider clock, CancellationToken requestToken)
    {
        using (worker)
        using (var timeout = new CancellationTokenSource(Deadline, clock))
        using (var stop = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, requestToken))
        {
            var token = stop.Token;
            var send = SendAsync(worker.Input, header, bytes, token);
            var output = ReadOutputAsync(worker.Output, token);
            // Do not retain or report stderr. Native libraries may write to it.
            var errors = worker.Errors.CopyToAsync(Stream.Null, token);
            var exit = worker.WaitForExitAsync(token);
            var memory = ObserveMemoryAsync(worker, maxMemoryMb * 1024L * 1024L, clock, token);
            Task[] tasks = [send, output, errors, exit, memory];
            var pending = tasks.ToList();

            try
            {
                while (pending.Count > 0)
                {
                    var completed = await Task.WhenAny(pending).WaitAsync(token).ConfigureAwait(false);
                    await completed.ConfigureAwait(false);
                    pending.Remove(completed);
                }

                if (worker.ExitCode != 0)
                    throw new DocumentCaptureServiceException(422, "DOCUMENT_UNREADABLE");

                return await output.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!requestToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                throw new DocumentCaptureServiceException(503, "DOCUMENT_BUSY");
            }
            finally
            {
                await stop.CancelAsync().ConfigureAwait(false);
                if (!worker.HasExited)
                {
                    worker.KillTree();
                    await worker.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }

                // Observe work on all redirected streams before closing the process handles.
                try
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The first failure is propagated above.
                }
            }
        }
    }

    private static async Task SendAsync(Stream stdin, CaptureWorkerHeader header, ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        await using (stdin)
        {
            try
            {
                await CaptureWorkerProtocol.WriteAsync(stdin, header, bytes, token).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // A child may close stdin early. Its exit code and JSON determine the result.
            }
        }
    }

    private static async Task<byte[]> ReadOutputAsync(Stream stdout, CancellationToken token)
    {
        try
        {
            return await CaptureWorkerProtocol.ReadBoundedAsync(stdout, CaptureWorkerProtocol.MaxOutputBytes, token)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            throw new DocumentCaptureServiceException(422, "DOCUMENT_TOO_COMPLEX");
        }
    }

    private static async Task ObserveMemoryAsync(ICaptureWorkerProcess worker, long limitBytes, TimeProvider clock, CancellationToken token)
    {
        while (!worker.HasExited)
        {
            if (worker.WorkingSetBytes > limitBytes)
                throw new DocumentCaptureServiceException(422, "DOCUMENT_TOO_COMPLEX");
            await Task.Delay(MemorySampleInterval, clock, token).ConfigureAwait(false);
        }
    }
}
