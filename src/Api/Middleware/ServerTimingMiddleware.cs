// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Api.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Api.Middleware;

/// <summary>
/// Emits Server-Timing (app / db / d1prep) at header time via Response.OnStarting.
/// Must be the outermost middleware so app;dur covers the full pipeline to headers.
/// </summary>
public sealed class ServerTimingMiddleware
{
    public const string HeaderName = "Server-Timing";
    public const long SlowAppThresholdMs = 1000;

    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ServerTimingMiddleware> _logger;

    public ServerTimingMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<ServerTimingMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ServerTimingOptions.IsEnabled(_configuration))
        {
            await _next(context);
            return;
        }

        var accumulator = new ServerTimingAccumulator();
        context.Items[ServerTimingAccumulator.HttpContextItemsKey] = accumulator;
        var sw = Stopwatch.StartNew();

        context.Response.OnStarting(() =>
        {
            sw.Stop();
            var appMs = sw.ElapsedMilliseconds;
            var header = BuildHeader(appMs, accumulator);
            if (!string.IsNullOrEmpty(header))
            {
                // Append if a prior hop somehow set the header (should not happen inside the container).
                if (context.Response.Headers.TryGetValue(HeaderName, out var existing)
                    && !string.IsNullOrWhiteSpace(existing))
                {
                    context.Response.Headers[HeaderName] = existing + ", " + header;
                }
                else
                {
                    context.Response.Headers[HeaderName] = header;
                }
            }

            if (appMs > SlowAppThresholdMs)
            {
                LogSlow(context, appMs, accumulator);
            }

            return Task.CompletedTask;
        });

        await _next(context);
    }

    public static string BuildHeader(long appMs, ServerTimingAccumulator accumulator)
    {
        var sb = new StringBuilder(128);
        AppendMetric(sb, "app", appMs, null);
        AppendMetric(
            sb,
            "db",
            accumulator.DbMilliseconds,
            accumulator.DbQueryCount == 1
                ? "1 query"
                : string.Create(CultureInfo.InvariantCulture, $"{accumulator.DbQueryCount} queries"));
        if (accumulator.HasD1Prep)
            AppendMetric(sb, "d1prep", accumulator.D1PrepMilliseconds, null);
        return sb.ToString();
    }

    private static void AppendMetric(StringBuilder sb, string name, long durMs, string? desc)
    {
        if (sb.Length > 0)
            sb.Append(", ");
        sb.Append(name);
        sb.Append(CultureInfo.InvariantCulture, $";dur={durMs}");
        if (!string.IsNullOrEmpty(desc))
        {
            sb.Append(";desc=\"");
            sb.Append(desc.Replace("\"", "", StringComparison.Ordinal));
            sb.Append('"');
        }
    }

    private void LogSlow(HttpContext context, long appMs, ServerTimingAccumulator accumulator)
    {
        var route = ResolveRouteTemplate(context);
        _logger.LogWarning(
            "SlowRequest route={Route} status={Status} app_ms={AppMs} db_ms={DbMs} db_queries={DbQueries} d1prep_ms={D1PrepMs}",
            route,
            context.Response.StatusCode,
            appMs,
            accumulator.DbMilliseconds,
            accumulator.DbQueryCount,
            accumulator.HasD1Prep ? accumulator.D1PrepMilliseconds : (long?)null);
    }

    internal static string ResolveRouteTemplate(HttpContext context)
    {
        if (context.GetEndpoint() is RouteEndpoint routeEndpoint)
        {
            var raw = routeEndpoint.RoutePattern.RawText;
            if (!string.IsNullOrWhiteSpace(raw))
                return raw.TrimStart('/');
        }

        // Fallback: path without query (never log query string).
        return context.Request.Path.HasValue ? context.Request.Path.Value!.TrimStart('/') : "";
    }
}
