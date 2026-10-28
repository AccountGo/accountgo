// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Modules;
using Dto.Subscription;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Services.Billing;
using Xunit;

namespace AccountGo.BackendTests.Modules;

public class PlanCatalogTests
{
    private const string SecretKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private sealed class StubPlanCatalog : IPlanCatalog
    {
        public IReadOnlyList<PlanDefinitionDto> GetPlans() => new[]
        {
            new PlanDefinitionDto { Key = "stub_a", Name = "Stub A", IncludedModules = { "sales" }, RequiredModules = { "sales" }, SelfService = true, MonthlyPrice = 1m },
            new PlanDefinitionDto { Key = "stub_b", Name = "Stub B", IncludedModules = { "sales" }, RequiredModules = { "sales" }, SelfService = false },
        };
    }

    private sealed class PlanCatalogModule : IAccountGoModule
    {
        public string Name => "plan-catalog-test-module";
        public void ConfigureServices(WebApplicationBuilder builder) =>
            builder.Services.AddSingleton<IPlanCatalog, StubPlanCatalog>();
        public void MapEndpoints(WebApplication app) { }
    }

    [Fact]
    public async Task CommunityBuild_PlansEndpoint_ServesTheSingleUnpricedCommunityPlan()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "accountgo_comm_plans_" + Path.GetRandomFileName() + ".db");
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DatabaseProvider", "Sqlite");
            builder.UseSetting("ConnectionStrings:SqliteConnection", "Data Source=" + dbPath);
            builder.UseSetting("SECRET_KEY", SecretKey);
            // Self-service filtering must not reach the community plan.
            builder.UseSetting("ENTITLEMENTS_ENABLED", "true");
            builder.UseSetting("ENTITLEMENTS_SELF_SERVICE_PLANS", "invoicing");
        });

        Assert.IsType<CommunityPlanCatalog>(factory.Services.GetRequiredService<IPlanCatalog>());
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/Subscription/plans");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var plan = Assert.Single(body.RootElement.EnumerateArray());
        Assert.Equal("community", plan.GetProperty("key").GetString());
        Assert.Equal("Community", plan.GetProperty("name").GetString());
        Assert.False(plan.GetProperty("selfService").GetBoolean());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("monthlyPrice").ValueKind);
        Assert.Equal(CommunityPlanCatalog.AllModules, plan.GetProperty("includedModules").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal(new[] { "sales" }, plan.GetProperty("requiredModules").EnumerateArray().Select(m => m.GetString()));
    }

    [Fact]
    public async Task HostWithModule_PlansEndpoint_ServesTheModuleCatalogUnchanged()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "accountgo_mod_plans_" + Path.GetRandomFileName() + ".db");
        var app = AccountGoApp.BuildApp([
            // Controllers are discovered from the application assembly; under the test runner that is not Api.
            "--applicationName=Api",
            "--Environment=Development",
            "--DatabaseProvider=Sqlite",
            $"--ConnectionStrings:SqliteConnection=Data Source={dbPath}",
            $"--SECRET_KEY={SecretKey}",
            "--urls=http://127.0.0.1:0"
        ], [new PlanCatalogModule()]);

        await app.StartAsync();
        try
        {
            Assert.IsType<StubPlanCatalog>(app.Services.GetRequiredService<IPlanCatalog>());
            Assert.Single(app.Services.GetServices<IPlanCatalog>());

            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
            var response = await client.GetAsync("/api/Subscription/plans");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var plans = body.RootElement.EnumerateArray().ToList();
            Assert.Equal(new[] { "stub_a", "stub_b" }, plans.Select(p => p.GetProperty("key").GetString()));
            Assert.Equal(new[] { true, false }, plans.Select(p => p.GetProperty("selfService").GetBoolean()));
            Assert.Equal(1m, plans[0].GetProperty("monthlyPrice").GetDecimal());
            Assert.Equal(JsonValueKind.Null, plans[1].GetProperty("monthlyPrice").ValueKind);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
