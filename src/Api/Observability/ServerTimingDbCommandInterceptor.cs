// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Api.Observability;

/// <summary>
/// Accumulates EF command durations into the current request's Server-Timing bucket.
/// Silent no-op when there is no HttpContext (startup, schema upgrade, background work).
/// </summary>
public sealed class ServerTimingDbCommandInterceptor : DbCommandInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ServerTimingDbCommandInterceptor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        StoreStart(eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        StoreStart(eventData);
        return new ValueTask<InterceptionResult<DbDataReader>>(result);
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        Record(eventData);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Record(eventData);
        return new ValueTask<DbDataReader>(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        StoreStart(eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        StoreStart(eventData);
        return new ValueTask<InterceptionResult<int>>(result);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        Record(eventData);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Record(eventData);
        return new ValueTask<int>(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        StoreStart(eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        StoreStart(eventData);
        return new ValueTask<InterceptionResult<object>>(result);
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        Record(eventData);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        Record(eventData);
        return new ValueTask<object?>(result);
    }

    private static void StoreStart(CommandEventData eventData)
    {
        // Duration comes from CommandExecutedEventData.Duration; start marker unused.
        _ = eventData;
    }

    private void Record(CommandExecutedEventData eventData)
    {
        var accumulator = TryGetAccumulator();
        if (accumulator == null)
            return;

        var ms = (long)Math.Round(eventData.Duration.TotalMilliseconds);
        accumulator.AddDbCommand(ms);
    }

    private ServerTimingAccumulator? TryGetAccumulator()
    {
        try
        {
            var http = _httpContextAccessor.HttpContext;
            if (http?.Items == null)
                return null;
            return http.Items[ServerTimingAccumulator.HttpContextItemsKey] as ServerTimingAccumulator;
        }
        catch
        {
            return null;
        }
    }
}
