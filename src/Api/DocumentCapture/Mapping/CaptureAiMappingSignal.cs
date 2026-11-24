// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
namespace Api.DocumentCaptures.Mapping;

/// <summary>
/// Wakes the mapping worker when a job is queued on this instance, so an idle worker waits on this instead of polling the database
/// every second. Jobs queued by another instance, and requeued jobs, are found by the worker's slower fallback check.
/// </summary>
public sealed class CaptureAiMappingSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled; one wake-up covers every job queued meanwhile.
        }
    }

    /// <summary>Waits until <see cref="Notify"/> is called or the fallback elapses. Returns true when it was signalled.</summary>
    public Task<bool> WaitAsync(TimeSpan fallback, CancellationToken cancellationToken) => _signal.WaitAsync(fallback, cancellationToken);
}
