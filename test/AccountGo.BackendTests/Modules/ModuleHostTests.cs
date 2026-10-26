// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Modules;
using Core.Entitlements;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests.Modules;

public class ModuleHostTests
{
    private sealed class HealthResponse
    {
        public string Status { get; set; } = string.Empty;
        public string[] Modules { get; set; } = [];
    }

    private sealed class StubModule : IAccountGoModule
    {
        public string Name => "stub-test-module";
        public bool ConfigureServicesCalled { get; private set; }
        public bool MapEndpointsCalled { get; private set; }

        public void ConfigureServices(WebApplicationBuilder builder)
        {
            ConfigureServicesCalled = true;
        }

        public void MapEndpoints(WebApplication app)
        {
            MapEndpointsCalled = true;
            app.MapGet("/api/stub-endpoint", () => Results.Ok(new { message = "stub" })).AllowAnonymous();
        }
    }

    [Fact]
    public async Task CommunityBuild_HealthCheck_ReturnsHealthyWithEmptyModules()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "accountgo_comm_health_" + Path.GetRandomFileName() + ".db");
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DatabaseProvider", "Sqlite");
            builder.UseSetting("ConnectionStrings:SqliteConnection", "Data Source=" + dbPath);
            builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        });

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var health = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(health);
        Assert.Equal("healthy", health.Status);
        Assert.Empty(health.Modules);
    }

    [Fact]
    public async Task HostWithModule_HealthCheck_ReturnsHealthyWithModuleName()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "accountgo_mod_health_" + Path.GetRandomFileName() + ".db");
        var app = AccountGoApp.BuildApp([
            "--Environment=Development",
            "--DatabaseProvider=Sqlite",
            $"--ConnectionStrings:SqliteConnection=Data Source={dbPath}",
            "--SECRET_KEY=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "--urls=http://127.0.0.1:0"
        ], [new StubModule()]);

        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
            var response = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var health = await response.Content.ReadFromJsonAsync<HealthResponse>();
            Assert.NotNull(health);
            Assert.Equal("healthy", health.Status);
            Assert.Contains("stub-test-module", health.Modules);

            var stubResponse = await client.GetAsync("/api/stub-endpoint");
            Assert.Equal(HttpStatusCode.OK, stubResponse.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public void CommunityEntitlementService_DoesNotHaveMfaFeature()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "accountgo_comm_ent_" + Path.GetRandomFileName() + ".db");
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DatabaseProvider", "Sqlite");
            builder.UseSetting("ConnectionStrings:SqliteConnection", "Data Source=" + dbPath);
            builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        });

        using var scope = factory.Services.CreateScope();
        var entitlementService = scope.ServiceProvider.GetRequiredService<IEntitlementService>();
        Assert.False(entitlementService.HasFeature(1, "mfa"));
    }
}
