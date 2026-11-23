#nullable enable
using System;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using Api.Data;
using Api.Middleware;
using Api.Options;
using Api.Service.Ai;
using Api.Tenancy;
using Core.Domain;
using Core.Domain.Security;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AccountGo.BackendTests.Ai
{
    public class McpCallerSessionMiddlewareTests : IAsyncLifetime
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), "accountgo_mcpsid_" + Guid.NewGuid().ToString("N") + ".db");
        private ServiceProvider _services = null!;
        private ApiDbContext _db = null!;
        private IMcpCallerAssertionService _assertionService = null!;
        private int _companyId;
        private const string CompanySlug = "mcp-sid-org";
        private const string UserId = "user-mcp-sid-1";
        private const string GateKey = "proj_api-key_mcp_sid_test";

        public async Task InitializeAsync()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, string?>("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
                })
                .Build();

            var sc = new ServiceCollection();
            sc.AddSingleton<IConfiguration>(config);
            sc.AddSingleton<IMcpCallerAssertionService, McpCallerAssertionService>();
            sc.AddSingleton<IOptions<AnterOptions>>(Microsoft.Extensions.Options.Options.Create(new AnterOptions { ApiKey = GateKey }));
            sc.AddScoped<ITenantContext, TenantContext>();
            sc.AddScoped<IMcpCallerSessionContext, McpCallerSessionContext>();
            sc.AddDbContext<ApiDbContext>(opts => opts.UseSqlite("Data Source=" + _dbPath));

            _services = sc.BuildServiceProvider();
            _db = _services.GetRequiredService<ApiDbContext>();
            await _db.Database.EnsureCreatedAsync();

            var company = new Company
            {
                Name = "MCP Sid Org",
                Slug = CompanySlug,
                CompanyCode = "MCPSID",
                ShortName = "MCPSID",
            };
            _db.Companies.Add(company);
            await _db.SaveChangesAsync();
            _companyId = company.Id;

            _db.CompanyMembers.Add(new CompanyMember
            {
                CompanyId = _companyId,
                UserId = UserId,
                Role = CompanyMemberRole.Owner,
                Status = CompanyMemberStatus.Active,
            });
            await _db.SaveChangesAsync();

            _assertionService = _services.GetRequiredService<IMcpCallerAssertionService>();
        }

        public async Task DisposeAsync()
        {
            await _services.DisposeAsync();
            foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            {
                if (File.Exists(file)) File.Delete(file);
            }
        }

        [Fact]
        public async Task ValidAssertionWithSessionId_PopulatesMcpCallerSessionContext()
        {
            using var scope = _services.CreateScope();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var sessionContext = scope.ServiceProvider.GetRequiredService<IMcpCallerSessionContext>();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();

            var assertion = _assertionService.MintAssertion(UserId, _companyId, CompanySlug, sessionId: "chat-sess-99");
            string? observedSid = null;
            var middleware = new TenantResolutionMiddleware(ctx =>
            {
                observedSid = ctx.RequestServices.GetRequiredService<IMcpCallerSessionContext>().SessionId;
                return Task.CompletedTask;
            });

            var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            http.Request.Path = "/mcp";
            http.Request.Headers[TenantResolutionMiddleware.McpServerKeyHeaderName] = GateKey;
            http.Request.Headers[McpCallerAssertionService.HeaderName] = assertion;

            await middleware.InvokeAsync(http, tenantContext, db);

            Assert.Equal(StatusCodes.Status200OK, http.Response.StatusCode);
            Assert.Equal("chat-sess-99", observedSid);
            Assert.Equal("chat-sess-99", sessionContext.SessionId);
        }

        [Fact]
        public async Task ValidAssertionWithoutSessionId_LeavesSessionIdNull()
        {
            using var scope = _services.CreateScope();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var sessionContext = scope.ServiceProvider.GetRequiredService<IMcpCallerSessionContext>();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();

            var assertion = _assertionService.MintAssertion(UserId, _companyId, CompanySlug, sessionId: null);
            var nextCalled = false;
            var middleware = new TenantResolutionMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            http.Request.Path = "/mcp";
            http.Request.Headers[TenantResolutionMiddleware.McpServerKeyHeaderName] = GateKey;
            http.Request.Headers[McpCallerAssertionService.HeaderName] = assertion;

            await middleware.InvokeAsync(http, tenantContext, db);

            Assert.True(nextCalled);
            Assert.Null(sessionContext.SessionId);
        }

        [Fact]
        public async Task TamperedAssertion_RejectsWith401_AndDoesNotPopulateSessionContext()
        {
            using var scope = _services.CreateScope();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var sessionContext = scope.ServiceProvider.GetRequiredService<IMcpCallerSessionContext>();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();

            var assertion = _assertionService.MintAssertion(UserId, _companyId, CompanySlug, sessionId: "chat-sess-99") + "x";
            var nextCalled = false;
            var middleware = new TenantResolutionMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            http.Request.Path = "/mcp";
            http.Request.Headers[TenantResolutionMiddleware.McpServerKeyHeaderName] = GateKey;
            http.Request.Headers[McpCallerAssertionService.HeaderName] = assertion;

            await middleware.InvokeAsync(http, tenantContext, db);

            Assert.False(nextCalled);
            Assert.Equal(StatusCodes.Status401Unauthorized, http.Response.StatusCode);
            Assert.Null(sessionContext.SessionId);
        }
    }
}
