// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Api.DocumentCaptures.Mapping;

/// <summary>Runs queued field-mapping jobs in the background. Registered only when AI mapping is enabled in configuration.</summary>
public sealed class DocumentCaptureAiMappingWorker(IServiceScopeFactory scopes, ILogger<DocumentCaptureAiMappingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var worked = false;
            try
            {
                using var scope = scopes.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<DocumentCaptureAiMappingProcessor>();
                worked = await processor.ProcessNextAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError("Capture AI mapping pass failed. FailureType={FailureType}", ex.GetType().Name);
            }

            if (!worked)
            {
                try
                {
                    await Task.Delay(IdleDelay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
