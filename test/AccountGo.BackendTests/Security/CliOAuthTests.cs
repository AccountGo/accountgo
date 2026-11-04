// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Api.Data;
using Api.Service;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests.Security;

public class CliOAuthTests : IDisposable
{
    private readonly string _dbPath;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public CliOAuthTests()
    {
        _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "accountgo_cli_oauth_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DatabaseProvider", "Sqlite");
            builder.UseSetting("ConnectionStrings:SqliteConnection", "Data Source=" + _dbPath);
            builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
            builder.UseSetting("JwtSettings:validIssuer", "AccountGo API");
            builder.UseSetting("JwtSettings:validAudience", "http://localhost:8001");
            builder.UseSetting("PUBLIC_WEB_URL", "http://localhost:5173");
        });
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        try { System.IO.File.Delete(_dbPath); } catch { /* ignore */ }
    }

    private async Task<(ApplicationUser User, string AccessToken)> SeedUserAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = $"cli-{Guid.NewGuid():N}@accountgo.dev",
            Email = $"cli-{Guid.NewGuid():N}@accountgo.dev",
            EmailConfirmed = true
        };
        await users.CreateAsync(user);
        var token = await auth.CreateToken(user, purpose: "access", amr: new[] { "pwd", "mfa" }, mfa: true, mfaTimestamp: DateTimeOffset.UtcNow);
        return (user, token.AccessToken);
    }

    private static (string Verifier, string Challenge) CreatePkce()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64Url(bytes);
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string AuthorizeQuery(string challenge, string redirectUri, string state = "state-1", string method = "S256")
    {
        var q = HttpUtility.ParseQueryString(string.Empty);
        q["response_type"] = "code";
        q["client_id"] = "accountgo-cli";
        q["redirect_uri"] = redirectUri;
        q["scope"] = "offline_access api";
        q["state"] = state;
        q["code_challenge"] = challenge;
        q["code_challenge_method"] = method;
        return q.ToString()!;
    }

    [Fact]
    public async Task Authorize_RejectsPlainPkceMethod()
    {
        var (_, challenge) = CreatePkce();
        var res = await _client.GetAsync("/api/auth/oauth/authorize?" + AuthorizeQuery(challenge, "http://127.0.0.1:9999/cb", method: "plain"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Authorize_RejectsBadClient()
    {
        var (_, challenge) = CreatePkce();
        var q = AuthorizeQuery(challenge, "http://127.0.0.1:9999/cb");
        q = q.Replace("accountgo-cli", "evil-cli");
        var res = await _client.GetAsync("/api/auth/oauth/authorize?" + q);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Authorize_RejectsNonLoopbackRedirect()
    {
        var (_, challenge) = CreatePkce();
        var res = await _client.GetAsync("/api/auth/oauth/authorize?" + AuthorizeQuery(challenge, "https://evil.example/cb"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Authorize_RedirectsToSpaConsentWithRequestId()
    {
        var (_, challenge) = CreatePkce();
        var res = await _client.GetAsync("/api/auth/oauth/authorize?" + AuthorizeQuery(challenge, "http://127.0.0.1:4242/oauth/callback"));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var location = res.Headers.Location?.ToString();
        Assert.NotNull(location);
        Assert.StartsWith("http://localhost:5173/cli/authorize?request_id=", location);
    }

    [Fact]
    public async Task FullFlow_ApproveExchangeRefreshAndDenyReplay()
    {
        var (user, webToken) = await SeedUserAsync();
        var (verifier, challenge) = CreatePkce();
        const string redirect = "http://127.0.0.1:4242/oauth/callback";
        const string state = "xyz";

        var authorize = await _client.GetAsync("/api/auth/oauth/authorize?" + AuthorizeQuery(challenge, redirect, state));
        var requestId = HttpUtility.ParseQueryString(authorize.Headers.Location!.Query).Get("request_id");
        Assert.False(string.IsNullOrEmpty(requestId));

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", webToken);

        var decide = await _client.PostAsJsonAsync("/api/auth/oauth/code", new
        {
            requestId,
            decision = "approve",
            deviceLabel = "CLI on test"
        });
        Assert.Equal(HttpStatusCode.OK, decide.StatusCode);
        var decideBody = await decide.Content.ReadFromJsonAsync<JsonElement>();
        var redirectUrl = decideBody.GetProperty("redirectUrl").GetString()!;
        var code = HttpUtility.ParseQueryString(new Uri(redirectUrl).Query).Get("code");
        var returnedState = HttpUtility.ParseQueryString(new Uri(redirectUrl).Query).Get("state");
        Assert.Equal(state, returnedState);
        Assert.False(string.IsNullOrEmpty(code));

        _client.DefaultRequestHeaders.Authorization = null;
        var tokenRes = await _client.PostAsync("/api/auth/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "accountgo-cli",
            ["code"] = code!,
            ["redirect_uri"] = redirect,
            ["code_verifier"] = verifier
        }));
        Assert.Equal(HttpStatusCode.OK, tokenRes.StatusCode);
        var token = await tokenRes.Content.ReadFromJsonAsync<JsonElement>();
        var access = token.GetProperty("access_token").GetString()!;
        var refresh = token.GetProperty("refresh_token").GetString()!;
        Assert.Equal("offline_access api", token.GetProperty("scope").GetString());

        // Port mismatch must fail.
        var badPort = await _client.PostAsync("/api/auth/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "accountgo-cli",
            ["code"] = code!,
            ["redirect_uri"] = "http://127.0.0.1:9999/oauth/callback",
            ["code_verifier"] = verifier
        }));
        Assert.Equal(HttpStatusCode.BadRequest, badPort.StatusCode);

        // Replay must fail and revoke family.
        var replay = await _client.PostAsync("/api/auth/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "accountgo-cli",
            ["code"] = code!,
            ["redirect_uri"] = redirect,
            ["code_verifier"] = verifier
        }));
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            var active = await db.RefreshTokens.CountAsync(t =>
                t.UserId == user.Id && t.Channel == RefreshTokenChannels.Cli && t.RevokedAt == null);
            Assert.Equal(0, active);
        }

        // Fresh approve for refresh + aud=cli denial tests
        var (verifier2, challenge2) = CreatePkce();
        var authorize2 = await _client.GetAsync("/api/auth/oauth/authorize?" + AuthorizeQuery(challenge2, redirect, "s2"));
        var requestId2 = HttpUtility.ParseQueryString(authorize2.Headers.Location!.Query).Get("request_id");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", webToken);
        var decide2 = await _client.PostAsJsonAsync("/api/auth/oauth/code", new { requestId = requestId2, decision = "approve" });
        var code2 = HttpUtility.ParseQueryString(new Uri((await decide2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("redirectUrl").GetString()!).Query).Get("code");
        _client.DefaultRequestHeaders.Authorization = null;
        var token2Res = await _client.PostAsync("/api/auth/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "accountgo-cli",
            ["code"] = code2!,
            ["redirect_uri"] = redirect,
            ["code_verifier"] = verifier2
        }));
        var token2 = await token2Res.Content.ReadFromJsonAsync<JsonElement>();
        access = token2.GetProperty("access_token").GetString()!;
        refresh = token2.GetProperty("refresh_token").GetString()!;

        var refreshRes = await _client.PostAsync("/api/auth/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = "accountgo-cli",
            ["refresh_token"] = refresh
        }));
        Assert.Equal(HttpStatusCode.OK, refreshRes.StatusCode);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var denied = await _client.GetAsync($"/api/auth/oauth/requests/{requestId2}");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var sessionsDenied = await _client.GetAsync("/api/auth/sessions");
        Assert.Equal(HttpStatusCode.Forbidden, sessionsDenied.StatusCode);
    }

    [Fact]
    public async Task Deny_ReturnsAccessDeniedWithOriginalState()
    {
        var (_, webToken) = await SeedUserAsync();
        var (_, challenge) = CreatePkce();
        const string redirect = "http://127.0.0.1:4242/oauth/callback";
        const string state = "deny-state";
        var authorize = await _client.GetAsync("/api/auth/oauth/authorize?" + AuthorizeQuery(challenge, redirect, state));
        var requestId = HttpUtility.ParseQueryString(authorize.Headers.Location!.Query).Get("request_id");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", webToken);
        var decide = await _client.PostAsJsonAsync("/api/auth/oauth/code", new { requestId, decision = "deny" });
        Assert.Equal(HttpStatusCode.OK, decide.StatusCode);
        var redirectUrl = (await decide.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("redirectUrl").GetString()!;
        var q = HttpUtility.ParseQueryString(new Uri(redirectUrl).Query);
        Assert.Equal("access_denied", q.Get("error"));
        Assert.Equal(state, q.Get("state"));
    }

    [Fact]
    public async Task ConcurrentDoubleRedeem_OnlyOneWins()
    {
        var (_, webToken) = await SeedUserAsync();
        var (verifier, challenge) = CreatePkce();
        const string redirect = "http://127.0.0.1:4242/oauth/callback";
        var authorize = await _client.GetAsync("/api/auth/oauth/authorize?" + AuthorizeQuery(challenge, redirect));
        var requestId = HttpUtility.ParseQueryString(authorize.Headers.Location!.Query).Get("request_id");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", webToken);
        var decide = await _client.PostAsJsonAsync("/api/auth/oauth/code", new { requestId, decision = "approve" });
        var code = HttpUtility.ParseQueryString(new Uri((await decide.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("redirectUrl").GetString()!).Query).Get("code");
        _client.DefaultRequestHeaders.Authorization = null;

        FormUrlEncodedContent Body() => new(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "accountgo-cli",
            ["code"] = code!,
            ["redirect_uri"] = redirect,
            ["code_verifier"] = verifier
        });

        using var c1 = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var c2 = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var t1 = c1.PostAsync("/api/auth/oauth/token", Body());
        var t2 = c2.PostAsync("/api/auth/oauth/token", Body());
        var results = await Task.WhenAll(t1, t2);
        var statuses = results.Select(r => r.StatusCode).ToArray();
        Assert.Contains(HttpStatusCode.OK, statuses);
        Assert.True(
            statuses.Contains(HttpStatusCode.BadRequest) || statuses.Contains(HttpStatusCode.OK) && statuses.Distinct().Count() == 1 && statuses[0] == HttpStatusCode.OK && statuses.Count(s => s == HttpStatusCode.OK) == 1
            || statuses.Count(s => s == HttpStatusCode.OK) == 1,
            $"Expected exactly one OK; got {string.Join(',', statuses)}");
        Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.OK));
    }

    [Fact]
    public async Task ExpiredCode_Rejected()
    {
        var (_, webToken) = await SeedUserAsync();
        var (verifier, challenge) = CreatePkce();
        const string redirect = "http://127.0.0.1:4242/oauth/callback";
        var authorize = await _client.GetAsync("/api/auth/oauth/authorize?" + AuthorizeQuery(challenge, redirect));
        var requestId = HttpUtility.ParseQueryString(authorize.Headers.Location!.Query).Get("request_id")!;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", webToken);
        var decide = await _client.PostAsJsonAsync("/api/auth/oauth/code", new { requestId, decision = "approve" });
        var code = HttpUtility.ParseQueryString(new Uri((await decide.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("redirectUrl").GetString()!).Query).Get("code")!;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            var row = await db.OAuthAuthorizationRequests.FirstAsync(r => r.Id == requestId);
            row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        _client.DefaultRequestHeaders.Authorization = null;
        var tokenRes = await _client.PostAsync("/api/auth/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "accountgo-cli",
            ["code"] = code,
            ["redirect_uri"] = redirect,
            ["code_verifier"] = verifier
        }));
        Assert.Equal(HttpStatusCode.BadRequest, tokenRes.StatusCode);
    }

    [Fact]
    public async Task MfaUnsatisfied_CannotObtainCode()
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "mfa-fail@accountgo.dev",
            Email = "mfa-fail@accountgo.dev",
            EmailConfirmed = true
        };
        await users.CreateAsync(user);
        // Explicitly unsatisfied MFA on a timed token.
        var stale = await auth.CreateToken(
            user,
            purpose: "access",
            lifetime: TimeSpan.FromHours(2),
            amr: new[] { "pwd" },
            mfa: false,
            mfaTimestamp: DateTimeOffset.UtcNow.AddHours(-2),
            extraClaims: new[] { new System.Security.Claims.Claim("iat", DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds().ToString()) });

        var (_, challenge) = CreatePkce();
        var authorize = await _client.GetAsync("/api/auth/oauth/authorize?" + AuthorizeQuery(challenge, "http://127.0.0.1:4242/oauth/callback"));
        var requestId = HttpUtility.ParseQueryString(authorize.Headers.Location!.Query).Get("request_id");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", stale.AccessToken);
        var decide = await _client.PostAsJsonAsync("/api/auth/oauth/code", new { requestId, decision = "approve" });
        Assert.Equal(HttpStatusCode.Forbidden, decide.StatusCode);
        var body = await decide.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("mfa_step_up_required", body.GetProperty("code").GetString());
    }
}
