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
using Api.Service.D1Sync;
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
        var options = new DbContextOptionsBuilder<ApiDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var context = new ApiDbContext(options);
        var logger = new Mock<ILogger<InternalSyncController>>();
        return new InternalSyncController(context, config, logger.Object);
    }

    [Fact]
    public void InternalSyncController_Build_Returns401_WithoutKey()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SECRET_KEY"] = TestSecret
        }).Build();

        var controller = CreateController(config);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = controller.GetBuildInfo();

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(401, unauthorized.StatusCode);
    }

    [Fact]
    public void InternalSyncController_Build_Returns401_WithInvalidKey()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SECRET_KEY"] = TestSecret
        }).Build();

        var controller = CreateController(config);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["x-internal-key"] = "wrong-key";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = controller.GetBuildInfo();

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(401, unauthorized.StatusCode);
    }

    [Fact]
    public void InternalSyncController_Build_Returns200AndSha_WithValidKey()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SECRET_KEY"] = TestSecret
        }).Build();

        var controller = CreateController(config);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["x-internal-key"] = TestSecret;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = controller.GetBuildInfo();

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);

        // Value contains sha
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("sha", out var shaProp));
        Assert.False(string.IsNullOrWhiteSpace(shaProp.GetString()));
    }

    [Fact]
    public void D1SyncMiddleware_SplitStatementsIntoChunks_CompressesAndSplitsAtConfiguredLimit_AndRoundTrips()
    {
        // Arrange: Generate statements totaling > 60 KB
        var statements = new List<string>();
        for (int i = 0; i < 300; i++)
        {
            statements.Add($"INSERT INTO \"TestTable\" (\"Id\", \"Name\", \"Description\", \"CompanyId\") VALUES ({i}, 'Test Item {i}', 'A long description string to fill up bytes in the statement payload number {i}', 1);");
        }

        var fullJson = JsonSerializer.Serialize(statements);
        var fullBytes = Encoding.UTF8.GetBytes(fullJson);
        var fullB64 = Convert.ToBase64String(fullBytes);
        Assert.True(fullB64.Length > 32768, $"Full payload was {fullB64.Length}, expected > 32768");

        // Act
        var chunks = D1SyncMiddleware.SplitStatementsIntoChunks(statements, 1024);

        // Assert: Multiple compressed chunks, all within the configured limit
        Assert.True(chunks.Count >= 2, $"Expected at least 2 chunks, got {chunks.Count}");
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Length <= 1024, $"Chunk length {chunk.Length} exceeded 1024");
        }

        // Join before decoding and decompressing.
        var reconstructed = DecodeChunks(chunks);

        Assert.Equal(statements.Count, reconstructed.Count);
        for (int i = 0; i < statements.Count; i++)
        {
            Assert.Equal(statements[i], reconstructed[i]);
        }
    }

    private class TestResponseFeature : Microsoft.AspNetCore.Http.Features.IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> callback, object state)> _callbacks = new();
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => false;

        public void OnStarting(Func<object, Task> callback, object state)
        {
            _callbacks.Add((callback, state));
        }

        public void OnCompleted(Func<object, Task> callback, object state) { }

        public async Task FireOnStartingAsync()
        {
            foreach (var (cb, st) in _callbacks)
            {
                await cb(st);
            }
        }
    }

    [Fact]
    public async Task D1SyncMiddleware_InvokeAsync_SetsSplitHeaders_WhenExceeding32KB()
    {
        // Arrange
        var statements = new List<string>();
        for (int i = 0; i < 2000; i++)
        {
            statements.Add($"INSERT INTO \"Item\" (\"Id\", \"No\", \"Description\", \"CompanyId\") VALUES ({i}, 'ITM-{Guid.NewGuid():N}', '{Guid.NewGuid():N}', 1);");
        }

        var syncContextMock = new Mock<ID1SyncContext>();
        syncContextMock.Setup(s => s.GetStatements()).Returns(statements);

        var httpContext = new DefaultHttpContext();
        var responseFeature = new TestResponseFeature();
        httpContext.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(responseFeature);
        httpContext.Response.StatusCode = 200;

        var middleware = new D1SyncMiddleware(ctx => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(httpContext, syncContextMock.Object);
        await responseFeature.FireOnStartingAsync();

        // Assert
        Assert.False(httpContext.Response.Headers.ContainsKey("x-d1-sync-sql"));
        Assert.Equal("gzip", httpContext.Response.Headers["x-d1-sync-encoding"].ToString());
        Assert.True(httpContext.Response.Headers.ContainsKey("x-d1-sync-sql-count"));

        int count = int.Parse(httpContext.Response.Headers["x-d1-sync-sql-count"]!);
        Assert.True(count >= 2);

        var chunks = new List<string>();
        for (int i = 0; i < count; i++)
        {
            var headerKey = $"x-d1-sync-sql-{i}";
            Assert.True(httpContext.Response.Headers.ContainsKey(headerKey));
            var chunkB64 = httpContext.Response.Headers[headerKey].ToString();
            Assert.True(chunkB64.Length <= 32768);

            chunks.Add(chunkB64);
        }

        Assert.Equal(statements, DecodeChunks(chunks));
    }

    [Fact]
    public async Task D1SyncMiddleware_InvokeAsync_SetsSingleHeader_WhenUnder32KB()
    {
        // Arrange
        var statements = new List<string>
        {
            "INSERT INTO \"Item\" (\"Id\", \"No\", \"CompanyId\") VALUES (1, 'ITM-1', 1);"
        };

        var syncContextMock = new Mock<ID1SyncContext>();
        syncContextMock.Setup(s => s.GetStatements()).Returns(statements);

        var httpContext = new DefaultHttpContext();
        var responseFeature = new TestResponseFeature();
        httpContext.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(responseFeature);
        httpContext.Response.StatusCode = 200;

        var middleware = new D1SyncMiddleware(ctx => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(httpContext, syncContextMock.Object);
        await responseFeature.FireOnStartingAsync();

        // Assert
        Assert.True(httpContext.Response.Headers.ContainsKey("x-d1-sync-sql"));
        Assert.Equal("gzip", httpContext.Response.Headers["x-d1-sync-encoding"].ToString());
        Assert.False(httpContext.Response.Headers.ContainsKey("x-d1-sync-sql-count"));

        var b64 = httpContext.Response.Headers["x-d1-sync-sql"].ToString();
        Assert.Equal(statements, DecodeChunks(new[] { b64 }));
    }

    private static List<string> DecodeChunks(IEnumerable<string> chunks)
    {
        using (var input = new MemoryStream(Convert.FromBase64String(string.Concat(chunks))))
        using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        using (var reader = new StreamReader(gzip, Encoding.UTF8))
            return JsonSerializer.Deserialize<List<string>>(reader.ReadToEnd())
                ?? throw new InvalidOperationException("Expected a JSON statement array.");
    }
}
