using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Controllers;
using Api.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AccountGo.ServicesTests;

public class ContainerRuntimePhase12Tests
{
    private const string TestSecret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private InternalSyncController CreateController(IConfiguration config)
    {
        var logger = new Mock<ILogger<InternalSyncController>>();
        return new InternalSyncController(config, logger.Object);
    }

    [Fact]
    public async Task InternalSyncController_Build_Returns401_WithoutKey()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SECRET_KEY"] = TestSecret
        }).Build();

        var controller = CreateController(config);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.GetBuildInfo();

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(401, unauthorized.StatusCode);
    }

    [Fact]
    public async Task InternalSyncController_Build_Returns401_WithInvalidKey()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SECRET_KEY"] = TestSecret
        }).Build();

        var controller = CreateController(config);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["x-internal-key"] = "wrong-key";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.GetBuildInfo();

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(401, unauthorized.StatusCode);
    }

    [Fact]
    public async Task InternalSyncController_Build_Returns200AndSha_WithValidKey()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SECRET_KEY"] = TestSecret
        }).Build();

        var controller = CreateController(config);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["x-internal-key"] = TestSecret;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.GetBuildInfo();

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);

        // Value contains sha
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("sha", out var shaProp));
        Assert.False(string.IsNullOrWhiteSpace(shaProp.GetString()));
    }

    [Theory]
    [InlineData("http://+:8001", "http://127.0.0.1:8001")]
    [InlineData("http://*:5000;https://*:5001", "http://127.0.0.1:5000")]
    [InlineData("http://0.0.0.0:8080", "http://127.0.0.1:8080")]
    [InlineData("http://localhost:8001", "http://localhost:8001")]
    [InlineData(null, "http://127.0.0.1:8001")]
    public void StartupWarmup_LoopbackUrl_ReplacesWildcardHosts(string? urls, string expected) =>
        Assert.Equal(expected, Api.Service.StartupWarmup.LoopbackUrl(urls));

    [Fact]
    public void StartupWarmup_IsOffUnlessConfigured()
    {
        Assert.False(Api.Service.StartupWarmup.IsEnabled(new ConfigurationBuilder().Build()));
        Assert.True(Api.Service.StartupWarmup.IsEnabled(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["WARMUP_ON_START"] = "true" }).Build()));
    }
}
