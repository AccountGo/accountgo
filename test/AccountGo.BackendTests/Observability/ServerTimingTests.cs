// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Api.Middleware;
using Api.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AccountGo.BackendTests.Observability;

public class ServerTimingTests
{
    [Fact]
    public async Task Middleware_EmitsAppAndDb_WithCorrectQueryCount()
    {
        using var host = await CreateHostAsync(enabled: true, runQueries: 2);
        var client = host.GetTestClient();
        var res = await client.GetAsync("/timing-probe");
        res.EnsureSuccessStatusCode();
        Assert.True(res.Headers.TryGetValues(ServerTimingMiddleware.HeaderName, out var values));
        var header = string.Join(", ", values!);
        Assert.Contains("app;dur=", header);
        Assert.Contains("db;dur=", header);
        Assert.Contains("desc=\"2 queries\"", header);
        Assert.DoesNotContain("d1prep", header);
    }

    [Fact]
    public async Task Middleware_AbsentWhenDisabled()
    {
        using var host = await CreateHostAsync(enabled: false, runQueries: 1);
        var client = host.GetTestClient();
        var res = await client.GetAsync("/timing-probe");
        res.EnsureSuccessStatusCode();
        Assert.False(res.Headers.Contains(ServerTimingMiddleware.HeaderName));
    }

    [Fact]
    public async Task Middleware_IncludesD1Prep_WhenAccumulatorSet()
    {
        using var host = await CreateHostAsync(enabled: true, runQueries: 0, setD1PrepMs: 7);
        var client = host.GetTestClient();
        var res = await client.GetAsync("/timing-probe");
        res.EnsureSuccessStatusCode();
        var header = string.Join(", ", res.Headers.GetValues(ServerTimingMiddleware.HeaderName));
        Assert.Contains("d1prep;dur=7", header);
    }

    [Fact]
    public void DbInterceptor_OutsideHttpContext_DoesNotThrow()
    {
        // Mirrors startup / SqliteSchemaUpgrader / background DbContext use: accessor has no HttpContext.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton<ServerTimingDbCommandInterceptor>();
        services.AddDbContext<ProbeDbContext>((sp, options) =>
        {
            options.UseSqlite("Data Source=:memory:");
            options.AddInterceptors(sp.GetRequiredService<ServerTimingDbCommandInterceptor>());
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();
        db.Database.OpenConnection();
        db.Database.EnsureCreated();

        var ex = Record.Exception(() =>
        {
            db.Rows.Add(new ProbeRow { Name = "x" });
            db.SaveChanges();
            _ = db.Rows.Count();
        });
        Assert.Null(ex);
    }

    [Fact]
    public void BuildHeader_FormatsSegmentsWithoutSensitiveData()
    {
        var acc = new ServerTimingAccumulator();
        acc.AddDbCommand(4);
        acc.AddDbCommand(6);
        acc.SetD1Prep(2);
        var header = ServerTimingMiddleware.BuildHeader(15, acc);
        Assert.Equal("app;dur=15, db;dur=10;desc=\"2 queries\", d1prep;dur=2", header);
        Assert.DoesNotContain("SELECT", header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Company", header, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IHost> CreateHostAsync(bool enabled, int runQueries, long? setD1PrepMs = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ServerTimingOptions.EnvKey] = enabled ? "true" : "false"
            })
            .Build();

        var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddSingleton<IConfiguration>(config);
                    services.AddHttpContextAccessor();
                    services.AddSingleton<ServerTimingDbCommandInterceptor>();
                    services.AddLogging();
                    services.AddRouting();
                });
                web.Configure(app =>
                {
                    app.UseMiddleware<ServerTimingMiddleware>();
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/timing-probe", async (HttpContext ctx) =>
                        {
                            if (setD1PrepMs is long prep
                                && ctx.Items[ServerTimingAccumulator.HttpContextItemsKey] is ServerTimingAccumulator acc)
                            {
                                acc.SetD1Prep(prep);
                            }

                            if (runQueries > 0
                                && ctx.Items[ServerTimingAccumulator.HttpContextItemsKey] is ServerTimingAccumulator timing)
                            {
                                for (var i = 0; i < runQueries; i++)
                                    timing.AddDbCommand(1);
                            }

                            await ctx.Response.WriteAsync("ok");
                        });
                    });
                });
            })
            .StartAsync();

        return host;
    }

    private sealed class ProbeDbContext : DbContext
    {
        public ProbeDbContext(DbContextOptions<ProbeDbContext> options) : base(options) { }
        public DbSet<ProbeRow> Rows => Set<ProbeRow>();
    }

    private sealed class ProbeRow
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }
}
