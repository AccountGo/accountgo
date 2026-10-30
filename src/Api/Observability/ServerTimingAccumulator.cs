// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Threading;

namespace Api.Observability;

/// <summary>Per-request Server-Timing accumulator (HttpContext.Items).</summary>
public sealed class ServerTimingAccumulator
{
    public const string HttpContextItemsKey = "AccountGo.ServerTiming";

    private long _dbMilliseconds;
    private int _dbQueryCount;

    public void AddDbCommand(long milliseconds)
    {
        if (milliseconds < 0)
            milliseconds = 0;
        Interlocked.Add(ref _dbMilliseconds, milliseconds);
        Interlocked.Increment(ref _dbQueryCount);
    }

    public long DbMilliseconds => Interlocked.Read(ref _dbMilliseconds);
    public int DbQueryCount => Volatile.Read(ref _dbQueryCount);
}
