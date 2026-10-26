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
}
