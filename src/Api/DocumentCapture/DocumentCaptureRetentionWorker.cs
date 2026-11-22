// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Api.DocumentCaptures;

public sealed class DocumentCaptureRetentionWorker(IServiceScopeFactory scopes, ILogger<DocumentCaptureRetentionWorker> logger,
    IOptions<DocumentCaptureOptions> options, TimeProvider? timeProvider = null) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startDelay = TimeSpan.FromSeconds(options.Value.RetentionStartDelaySeconds);
        if (startDelay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(startDelay, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var retention = scope.ServiceProvider.GetRequiredService<DocumentCaptureRetentionService>();
                var deleted = await retention.PurgeAsync(stoppingToken).ConfigureAwait(false);
                if (deleted > 0)
                    logger.LogInformation("Document capture workspace cleanup removed {CaptureCount} captures.", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Document capture retention pass failed.");
            }

            await Task.Delay(Interval, _timeProvider, stoppingToken).ConfigureAwait(false);
        }
    }
}
