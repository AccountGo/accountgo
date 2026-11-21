// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Api.DocumentCaptures;

public sealed class DocumentCaptureRetentionWorker(IServiceScopeFactory scopes, ILogger<DocumentCaptureRetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var retention = scope.ServiceProvider.GetRequiredService<DocumentCaptureRetentionService>();
                var deleted = await retention.PurgeAsync(DateTime.UtcNow, stoppingToken).ConfigureAwait(false);
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

            await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
        }
    }
}
