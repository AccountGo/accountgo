// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Data;
using Api.Filters;
using Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests.Security;

public class MfaSeamTests
{
    private static WebApplicationFactory<Program> CreateFactory(string dbPath)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DatabaseProvider", "Sqlite");
            builder.UseSetting("ConnectionStrings:SqliteConnection", "Data Source=" + dbPath);
            builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        });
    }

    [Fact]
    public async Task CommunityMfaGate_AlwaysReturnsNotRequired()
    {
        var gate = new CommunityMfaGate();
        var user = new ApplicationUser { Id = "test-user-1", UserName = "test@example.com" };

        var decision = await gate.EvaluateAfterFactorAAsync(user, "otp", null);

        Assert.Equal(MfaGateResult.NotRequired, decision.Result);
        Assert.Null(decision.Policy);
        Assert.Empty(decision.Methods);
    }

    [Fact]
    public async Task CommunityStepUpHandler_AlwaysSucceeds()
    {
        var handler = new CommunityStepUpHandler();
        var requirement = new MfaStepUpRequirement();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-1"),
            new Claim(ClaimTypes.Email, "user1@example.com")
        }, "TestAuth"));

        var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);
        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task SecurityCapabilities_OnCommunity_ReturnsDisabledCapabilities()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "accountgo_mfa_caps_" + Path.GetRandomFileName() + ".db");
        using var factory = CreateFactory(dbPath);
        using var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var authService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "caps-user@accountgo.dev",
                Email = "caps-user@accountgo.dev",
                EmailConfirmed = true
            };
            await userManager.CreateAsync(user);

            var token = await authService.CreateToken(user, populateExp: false);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        }

        var response = await client.GetAsync("/api/security/capabilities");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(doc.GetProperty("mfaAvailable").GetBoolean());
        Assert.False(doc.GetProperty("upgradeAvailable").GetBoolean());
        Assert.False(doc.GetProperty("enrolled").GetBoolean());
        Assert.Equal(0, doc.GetProperty("methods").GetArrayLength());
    }

    [Fact]
    public async Task PendingTokenMiddleware_BlocksBusinessEndpointsAndMcp_AllowsAllowlist()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "accountgo_mfa_pending_" + Path.GetRandomFileName() + ".db");
        using var factory = CreateFactory(dbPath);
        using var client = factory.CreateClient();

        string pendingToken;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var authService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "pending-user@accountgo.dev",
                Email = "pending-user@accountgo.dev",
                EmailConfirmed = true
            };
            await userManager.CreateAsync(user);

            var token = await authService.CreateToken(user, purpose: "mfa_pending");
            pendingToken = token.AccessToken;
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pendingToken);

        // 1. Business endpoint (Dashboard) must be blocked with 403 and code = mfa_challenge_required
        var bizResponse = await client.GetAsync("/api/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, bizResponse.StatusCode);
        var bizJson = await bizResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(bizJson.TryGetProperty("code", out var codeProp));
        Assert.Equal("mfa_challenge_required", codeProp.GetString());

        // 2. /mcp must be blocked with 403 and code = mfa_challenge_required
        var mcpResponse = await client.PostAsync("/mcp", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Forbidden, mcpResponse.StatusCode);
        var mcpJson = await mcpResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(mcpJson.TryGetProperty("code", out var mcpCodeProp));
        Assert.Equal("mfa_challenge_required", mcpCodeProp.GetString());

        // 3. Allowlisted /api/auth/logout is NOT rejected with mfa_challenge_required
        var logoutResponse = await client.PostAsync("/api/auth/logout", null);
        Assert.NotEqual(HttpStatusCode.Forbidden, logoutResponse.StatusCode);
    }

    [Fact]
    public async Task EnrollTokenMiddleware_BlocksBusinessEndpoints_AllowsMeEndpoint()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "accountgo_mfa_enroll_" + Path.GetRandomFileName() + ".db");
        using var factory = CreateFactory(dbPath);
        using var client = factory.CreateClient();

        string enrollToken;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var authService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "enroll-user@accountgo.dev",
                Email = "enroll-user@accountgo.dev",
                EmailConfirmed = true
            };
            await userManager.CreateAsync(user);

            var token = await authService.CreateToken(user, purpose: "mfa_enroll");
            enrollToken = token.AccessToken;
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", enrollToken);

        // 1. Business endpoint must be blocked with 403 and code = mfa_enrollment_required
        var bizResponse = await client.GetAsync("/api/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, bizResponse.StatusCode);
        var bizJson = await bizResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(bizJson.TryGetProperty("code", out var codeProp));
        Assert.Equal("mfa_enrollment_required", codeProp.GetString());

        // 2. Allowlisted /api/auth/me is NOT blocked by middleware (passes to controller -> 200 OK)
        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
    }

    [Fact]
    public void InertSchema_AllSecurityTablesAndColumns_ExistOnColdStart()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "accountgo_mfa_schema_" + Path.GetRandomFileName() + ".db");
        using var factory = CreateFactory(dbPath);
        using var scope = factory.Services.CreateScope();
        var identityContext = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
        var conn = identityContext.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            conn.Open();

        var expectedTables = new[]
        {
            "RefreshTokens",
            "DataProtectionKeys",
            "SecurityAuditEvents",
            "UserRecoveryCodes",
            "TrustedDevices",
            "UserWebAuthnCredentials",
            "WebAuthnChallenges"
        };

        foreach (var tbl in expectedTables)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tbl}';";
            var count = Convert.ToInt32(cmd.ExecuteScalar());
            Assert.True(count > 0, $"Table {tbl} must exist in SQLite database.");
        }

        // Verify columns on AspNetUsers
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('AspNetUsers') WHERE name='MfaReEnrollRequired';";
            Assert.True(Convert.ToInt32(cmd.ExecuteScalar()) > 0, "AspNetUsers.MfaReEnrollRequired column must exist.");
        }

        // Verify columns on CompanySetting
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('CompanySetting') WHERE name IN ('MfaPolicy', 'MfaGracePeriodEndsAt', 'RequireMfaEveryLogin');";
            Assert.Equal(3, Convert.ToInt32(cmd.ExecuteScalar()));
        }

        // Verify columns on RefreshTokens
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RefreshTokens') WHERE name IN ('MfaMethod', 'Amr', 'TrustedDeviceId');";
            Assert.Equal(3, Convert.ToInt32(cmd.ExecuteScalar()));
        }
    }

    [Fact]
    public void DbXmlRepository_StoresAndRetrievesDataProtectionKeys()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "accountgo_mfa_dp_" + Path.GetRandomFileName() + ".db");
        using var factory = CreateFactory(dbPath);
        using var scope = factory.Services.CreateScope();

        var repo = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.DataProtection.Repositories.IXmlRepository>();
        var friendlyName = "key-" + Guid.NewGuid().ToString("N");
        var xmlElement = new System.Xml.Linq.XElement("key",
            new System.Xml.Linq.XAttribute("id", friendlyName),
            new System.Xml.Linq.XElement("creationDate", DateTime.UtcNow.ToString("O")),
            new System.Xml.Linq.XElement("descriptor", "test-descriptor"));

        repo.StoreElement(xmlElement, friendlyName);

        var elements = repo.GetAllElements();
        Assert.Contains(elements, e => e.Attribute("id")?.Value == friendlyName);
    }

    [Fact]
    public async Task SecurityAuditService_PersistsAuditEvent()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "accountgo_mfa_audit_" + Path.GetRandomFileName() + ".db");
        using var factory = CreateFactory(dbPath);
        using var scope = factory.Services.CreateScope();

        var auditService = scope.ServiceProvider.GetRequiredService<ISecurityAuditService>();
        var identityContext = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();

        await auditService.LogEventAsync("test_event", "user-42", 1, "127.0.0.1", "TestAgent", new { action = "test" });

        var evt = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            identityContext.SecurityAuditEvents, e => e.EventType == "test_event");
        Assert.NotNull(evt);
        Assert.Equal("user-42", evt.UserId);
        Assert.Equal(1, evt.CompanyId);
        Assert.Equal("127.0.0.1", evt.IpAddress);
    }
}
