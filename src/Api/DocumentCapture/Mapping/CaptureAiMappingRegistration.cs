// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Api.DocumentCaptures.Mapping;

/// <summary>Development-only policy that allows every workspace, so the whole AI-assisted path can be run locally with the fake mapper.</summary>
public sealed class FakeAllowAllCaptureAiMappingPolicy : ICaptureAiMappingPolicy
{
    public Task<bool> IsAllowedAsync(int companyId, string kind, CancellationToken cancellationToken) => Task.FromResult(true);
}

public static class CaptureAiMappingRegistration
{
    /// <summary>
    /// Registers the mapping policy, signal, processor and mapper. Off by default: the default policy allows nothing and the default mapper
    /// returns the rules' fields. The allow-all policy is registered only with the fake mapper, which startup validation restricts to
    /// Development and tests; a host that adds consent and plan checks registers its own policy first and keeps it.
    /// </summary>
    public static IServiceCollection AddCaptureAiMapping(this IServiceCollection services, DocumentCaptureAiMappingOptions settings)
    {
        var useFake = settings.Enabled && string.Equals(settings.Mapper, "fake", StringComparison.OrdinalIgnoreCase);
        if (useFake)
            services.TryAddSingleton<ICaptureAiMappingPolicy, FakeAllowAllCaptureAiMappingPolicy>();
        services.TryAddSingleton<ICaptureAiMappingPolicy, DisabledCaptureAiMappingPolicy>();
        services.AddSingleton<CaptureAiMappingRateLimiter>();
        services.AddSingleton<CaptureAiMappingSignal>();
        services.AddScoped<DocumentCaptureAiMappingProcessor>();

        if (!settings.Enabled)
            services.AddSingleton<ICaptureFieldMapper, RuleOnlyFieldMapper>();
        else if (useFake)
            services.AddSingleton<ICaptureFieldMapper, FakeCaptureFieldMapper>();
        else
        {
            // Its own client with the short timeout, not the 10-minute assistant chat client.
            services.AddHttpClient(AnterCaptureFieldMapper.HttpClientName,
                client => client.Timeout = TimeSpan.FromSeconds(DocumentCaptureAiMappingOptions.MaxTimeoutSeconds + 4));
            services.AddSingleton<ICaptureFieldMapper>(serviceProvider => new AnterCaptureFieldMapper(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(AnterCaptureFieldMapper.HttpClientName),
                serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Api.Options.AnterOptions>>(),
                serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DocumentCaptureOptions>>(),
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AnterCaptureFieldMapper>>()));
        }

        if (settings.Enabled)
            services.AddHostedService<DocumentCaptureAiMappingWorker>();
        return services;
    }
}
