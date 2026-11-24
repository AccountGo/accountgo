// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.DocumentCaptures;
using Api.DocumentCaptures.Mapping;
using Api.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class CaptureAiMappingRegistrationTests
{
    private static ServiceProvider Build(DocumentCaptureAiMappingOptions settings, Action<IServiceCollection>? before = null, string? anterKey = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton(Options.Create(new DocumentCaptureOptions { AiMapping = settings }));
        services.AddSingleton(Options.Create(new AnterOptions { ApiKey = anterKey ?? "key" }));
        before?.Invoke(services);
        services.AddCaptureAiMapping(settings);
        return services.BuildServiceProvider();
    }

    private static bool HasWorker(ServiceProvider provider) =>
        provider.GetServices<IHostedService>().Any(service => service is DocumentCaptureAiMappingWorker);

    [Fact]
    public void ByDefaultNothingIsAllowedTheMapperReturnsTheRulesAndNoWorkerRuns()
    {
        using var provider = Build(new DocumentCaptureAiMappingOptions());

        Assert.IsType<DisabledCaptureAiMappingPolicy>(provider.GetRequiredService<ICaptureAiMappingPolicy>());
        Assert.IsType<RuleOnlyFieldMapper>(provider.GetRequiredService<ICaptureFieldMapper>());
        Assert.False(HasWorker(provider));
    }

    [Fact]
    public void TheAnterMapperStillAllowsNothingUntilAHostRegistersItsOwnPolicy()
    {
        using var provider = Build(new DocumentCaptureAiMappingOptions { Enabled = true });

        Assert.IsType<DisabledCaptureAiMappingPolicy>(provider.GetRequiredService<ICaptureAiMappingPolicy>());
        Assert.IsType<AnterCaptureFieldMapper>(provider.GetRequiredService<ICaptureFieldMapper>());
        Assert.True(HasWorker(provider));
    }

    [Fact]
    public void AHostPolicyRegisteredFirstIsKeptWithTheAnterMapperAndWithTheFakeMapper()
    {
        foreach (var mapper in new[] { "anter", "fake" })
        {
            using var provider = Build(new DocumentCaptureAiMappingOptions { Enabled = true, Mapper = mapper },
                services => services.AddSingleton<ICaptureAiMappingPolicy, HostPolicy>());

            Assert.IsType<HostPolicy>(provider.GetRequiredService<ICaptureAiMappingPolicy>());
        }
    }

    [Fact]
    public async Task TheFakeMapperInDevelopmentRunsTheWholePathWithAnAllowAllPolicy()
    {
        var settings = new DocumentCaptureAiMappingOptions { Enabled = true, Mapper = "fake" };
        Validate(settings, "Development");
        using var provider = Build(settings);

        Assert.IsType<FakeAllowAllCaptureAiMappingPolicy>(provider.GetRequiredService<ICaptureAiMappingPolicy>());
        Assert.IsType<FakeCaptureFieldMapper>(provider.GetRequiredService<ICaptureFieldMapper>());
        Assert.True(HasWorker(provider));
        Assert.True(await provider.GetRequiredService<ICaptureAiMappingPolicy>().IsAllowedAsync(1, "purchase-invoice", CancellationToken.None));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void TheFakeMapperAndItsAllowAllPolicyCannotStartOutsideDevelopmentAndTests(string environment)
    {
        var settings = new DocumentCaptureAiMappingOptions { Enabled = true, Mapper = "fake" };

        // Startup validation refuses the configuration before the application serves anything.
        Assert.Throws<InvalidOperationException>(() => Validate(settings, environment));
        // Without the fake mapper the allow-all policy is never registered, whatever the environment.
        using var provider = Build(new DocumentCaptureAiMappingOptions { Enabled = true, Mapper = "anter" });
        Assert.IsNotType<FakeAllowAllCaptureAiMappingPolicy>(provider.GetRequiredService<ICaptureAiMappingPolicy>());
    }

    [Fact]
    public void TheAllowAllPolicyIsNotRegisteredWhenAiMappingIsOffEvenIfTheFakeMapperIsNamed()
    {
        using var provider = Build(new DocumentCaptureAiMappingOptions { Enabled = false, Mapper = "fake" });

        Assert.IsType<DisabledCaptureAiMappingPolicy>(provider.GetRequiredService<ICaptureAiMappingPolicy>());
        Assert.IsType<RuleOnlyFieldMapper>(provider.GetRequiredService<ICaptureFieldMapper>());
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void TheFakeConfigurationIsAcceptedInDevelopmentAndTests(string environment) =>
        Validate(new DocumentCaptureAiMappingOptions { Enabled = true, Mapper = "fake" }, environment);

    private static void Validate(DocumentCaptureAiMappingOptions settings, string environment)
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environment);
        DocumentCaptureOptions.Validate(new DocumentCaptureOptions { AiMapping = settings }, env.Object);
    }

    private sealed class HostPolicy : ICaptureAiMappingPolicy
    {
        public Task<bool> IsAllowedAsync(int companyId, string kind, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
