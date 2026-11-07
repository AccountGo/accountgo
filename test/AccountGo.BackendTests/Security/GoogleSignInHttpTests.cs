// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Api.Service;
using Api.Service.Google;
using Dto.Security;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AccountGo.BackendTests.Security;

public class FakeMfaGate : IMfaGate
{
    public MfaGateDecision Decision { get; set; } = new(MfaGateResult.NotRequired, null, Array.Empty<string>());

    public Task<MfaGateDecision> EvaluateAfterFactorAAsync(
        ApplicationUser user,
        string factorAMethod,
        string? trustedDeviceRawToken,
        CancellationToken ct = default)
    {
        return Task.FromResult(Decision);
    }
}

public class FakeGoogleIdTokenValidator : IGoogleIdTokenValidator
{
    public Func<string, GoogleIdentity>? ValidateHandler { get; set; }
    public Exception? ExceptionToThrow { get; set; }

    public Task<GoogleIdentity> ValidateAsync(string idToken, CancellationToken ct = default)
    {
        if (ExceptionToThrow != null)
        {
            throw ExceptionToThrow;
        }

        if (ValidateHandler != null)
        {
            return Task.FromResult(ValidateHandler(idToken));
        }

        return Task.FromResult(new GoogleIdentity(
            Subject: "google-sub-default",
            Email: "default@gmail.com",
            EmailVerified: true,
            HostedDomain: null,
            Nonce: null,
            TokenId: "tok-default",
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30)
        ));
    }
}

public class CookieHandler : DelegatingHandler
{
    public CookieContainer CookieContainer { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var cookieHeader = CookieContainer.GetCookieHeader(request.RequestUri!);
        if (!string.IsNullOrEmpty(cookieHeader))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var setCookie in setCookies)
            {
                CookieContainer.SetCookies(request.RequestUri!, setCookie);
            }
        }

        return response;
    }
}

public class GoogleSignInHttpTests : IDisposable
{
    private readonly string _dbPath;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly FakeGoogleIdTokenValidator _fakeValidator = new();
    private readonly FakeMfaGate _fakeMfaGate = new();

    public GoogleSignInHttpTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "accountgo_google_test_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DatabaseProvider", "Sqlite");
            builder.UseSetting("ConnectionStrings:SqliteConnection", "Data Source=" + _dbPath);
            builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
            builder.UseSetting("Google:Enabled", "true");
            builder.UseSetting("Google:ClientId", "test-google-client-id");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGoogleIdTokenValidator>();
                services.AddSingleton<IGoogleIdTokenValidator>(_fakeValidator);
                services.RemoveAll<IMfaGate>();
                services.AddSingleton<IMfaGate>(_fakeMfaGate);
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        try { File.Delete(_dbPath); } catch { /* ignore */ }
    }

    private static async Task<ApplicationUser> SeedUserAsync(WebApplicationFactory<Program> factory, string email, bool confirmed = true, string? linkedSub = null)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = confirmed
        };
        var createRes = await userManager.CreateAsync(user);
        Assert.True(createRes.Succeeded);

        if (linkedSub != null)
        {
            var linkRes = await userManager.AddLoginAsync(user, new UserLoginInfo("Google", linkedSub, "Google"));
            Assert.True(linkRes.Succeeded);
        }

        return user;
    }

    private Task<ApplicationUser> SeedUserAsync(string email, bool confirmed = true, string? linkedSub = null)
        => SeedUserAsync(_factory, email, confirmed, linkedSub);

    private static HttpClient CreateBrowserClient(WebApplicationFactory<Program> factory, out CookieContainer cookieContainer)
    {
        var handler = new CookieHandler();
        cookieContainer = handler.CookieContainer;
        var client = factory.CreateDefaultClient(new Uri("https://localhost"), handler);
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        return client;
    }

    private HttpClient CreateBrowserClient(out CookieContainer cookieContainer)
        => CreateBrowserClient(_factory, out cookieContainer);

    private static WebApplicationFactory<Program> CreatePostgresFactory(
        PostgresTestDatabase postgres,
        FakeGoogleIdTokenValidator fakeValidator,
        FakeMfaGate fakeMfaGate)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DatabaseProvider", "Postgres");
            builder.UseSetting("ConnectionStrings:PostgresConnection", postgres.ConnectionString);
            builder.UseSetting("SEED_DEMO_DATA", "false");
            builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
            builder.UseSetting("Google:Enabled", "true");
            builder.UseSetting("Google:ClientId", "test-google-client-id");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGoogleIdTokenValidator>();
                services.AddSingleton<IGoogleIdTokenValidator>(fakeValidator);
                services.RemoveAll<IMfaGate>();
                services.AddSingleton<IMfaGate>(fakeMfaGate);
            });
        });
    }

    private static async Task<(string Nonce, string CookieHeader)> GetStartNonceAsync(HttpClient client, CookieContainer cookies)
    {
        var res = await client.GetAsync("/api/auth/google/start");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        var enabled = doc.RootElement.GetProperty("enabled").GetBoolean();
        Assert.True(enabled);
        var nonce = doc.RootElement.GetProperty("nonce").GetString();
        Assert.False(string.IsNullOrWhiteSpace(nonce));

        var uri = new Uri("https://localhost");
        var cookieList = cookies.GetCookies(uri);
        var gnonceCookie = cookieList["__Host-ag_gnonce"];
        Assert.NotNull(gnonceCookie);

        return (nonce!, gnonceCookie.Value);
    }

    // T1: Valid token, matching nonce, existing linked user, no MFA -> 200, session, refresh cookie, nonce cookie cleared
    [Fact]
    public async Task T1_ValidToken_MatchingNonce_ExistingLinkedUser_ReturnsSessionAndClearsNonceCookie()
    {
        var user = await SeedUserAsync("t1_user@gmail.com", confirmed: true, linkedSub: "sub-t1");
        using var client = CreateBrowserClient(out var cookies);

        var (nonce, _) = await GetStartNonceAsync(client, cookies);

        _fakeValidator.ValidateHandler = token => new GoogleIdentity(
            Subject: "sub-t1",
            Email: user.Email!,
            EmailVerified: true,
            HostedDomain: null,
            Nonce: nonce,
            TokenId: "tok-t1",
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30)
        );

        var res = await client.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("valid-token-t1"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.TryGetProperty("accessToken", out _));

        // Refresh cookie set
        var apiUri = new Uri("https://localhost/api");
        var postCookies = cookies.GetCookies(apiUri);
        Assert.NotNull(postCookies["accountgo_refresh_token"]);

        // Nonce cookie cleared
        var hostUri = new Uri("https://localhost");
        var hostCookies = cookies.GetCookies(hostUri);
        var clearedNonce = hostCookies["__Host-ag_gnonce"];
        Assert.True(clearedNonce == null || clearedNonce.Expired || string.IsNullOrEmpty(clearedNonce.Value));

        // Audit success event recorded
        using var scope = _factory.Services.CreateScope();
        var idDb = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
        var audit = await idDb.SecurityAuditEvents.FirstOrDefaultAsync(e => e.EventType == "auth.signin.success" && e.UserId == user.Id);
        Assert.NotNull(audit);
    }

    // T2: No nonce cookie -> 401 nonce_missing
    [Fact]
    public async Task T2_NoNonceCookie_Returns401NonceMissing()
    {
        await SeedUserAsync("t2_user@gmail.com", confirmed: true, linkedSub: "sub-t2");
        using var client = CreateBrowserClient(out _);

        _fakeValidator.ValidateHandler = token => new GoogleIdentity(
            Subject: "sub-t2",
            Email: "t2_user@gmail.com",
            EmailVerified: true,
            HostedDomain: null,
            Nonce: "some-nonce",
            TokenId: "tok-t2",
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30)
        );

        var res = await client.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("valid-token-t2"));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        Assert.Equal("nonce_missing", doc.RootElement.GetProperty("code").GetString());
    }

    // T3: Nonce cookie differs from token nonce -> 401 nonce_mismatch
    [Fact]
    public async Task T3_NonceMismatch_Returns401NonceMismatch()
    {
        await SeedUserAsync("t3_user@gmail.com", confirmed: true, linkedSub: "sub-t3");
        using var client = CreateBrowserClient(out var cookies);
        await GetStartNonceAsync(client, cookies);

        _fakeValidator.ValidateHandler = token => new GoogleIdentity(
            Subject: "sub-t3",
            Email: "t3_user@gmail.com",
            EmailVerified: true,
            HostedDomain: null,
            Nonce: "different-nonce-than-cookie",
            TokenId: "tok-t3",
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30)
        );

        var res = await client.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("valid-token-t3"));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        Assert.Equal("nonce_mismatch", doc.RootElement.GetProperty("code").GetString());
    }

    // T4: Validator throws InvalidJwtException -> 401 invalid_token
    [Fact]
    public async Task T4_InvalidJwtException_Returns401InvalidToken()
    {
        using var client = CreateBrowserClient(out var cookies);
        await GetStartNonceAsync(client, cookies);

        _fakeValidator.ExceptionToThrow = new InvalidJwtException("Signature verification failed");

        var res = await client.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("bad-token"));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        Assert.Equal("invalid_token", doc.RootElement.GetProperty("code").GetString());
    }

    // T5: email_verified false or empty sub -> 401 invalid_token
    [Theory]
    [InlineData(false, "sub-5")]
    [InlineData(true, "")]
    public async Task T5_UnverifiedEmailOrEmptySub_Returns401InvalidToken(bool emailVerified, string sub)
    {
        using var client = CreateBrowserClient(out var cookies);
        var (nonce, _) = await GetStartNonceAsync(client, cookies);

        _fakeValidator.ValidateHandler = token => new GoogleIdentity(
            Subject: sub,
            Email: "t5@gmail.com",
            EmailVerified: emailVerified,
            HostedDomain: null,
            Nonce: nonce,
            TokenId: "tok-t5",
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30)
        );

        var res = await client.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("token-t5"));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        Assert.Equal("invalid_token", doc.RootElement.GetProperty("code").GetString());
    }

    // T6: Hosted domain set and token hd differs -> 403 hosted_domain_mismatch
    [Fact]
    public async Task T6_HostedDomainMismatch_Returns403()
    {
        using var customFactory = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Google:HostedDomain", "company.com");
        });
        var handler = new CookieHandler();
        using var client = customFactory.CreateDefaultClient(new Uri("https://localhost"), handler);
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");

        // Request /start to get cookie
        var startRes = await client.GetAsync("/api/auth/google/start");
        using var startDoc = await JsonDocument.ParseAsync(await startRes.Content.ReadAsStreamAsync());
        var nonce = startDoc.RootElement.GetProperty("nonce").GetString();

        _fakeValidator.ValidateHandler = token => new GoogleIdentity(
            Subject: "sub-hd",
            Email: "user@wrongdomain.com",
            EmailVerified: true,
            HostedDomain: "wrongdomain.com",
            Nonce: nonce,
            TokenId: "tok-hd",
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30)
        );

        var res = await client.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("token-hd"));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        Assert.Equal("hosted_domain_mismatch", doc.RootElement.GetProperty("code").GetString());
    }

    // T7: Google:Enabled false -> 404 google_disabled; /start returns enabled: false
    [Fact]
    public async Task T7_GoogleDisabled_Returns404GoogleDisabled()
    {
        using var disabledFactory = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Google:Enabled", "false");
        });
        using var client = disabledFactory.CreateDefaultClient();
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");

        var startRes = await client.GetAsync("/api/auth/google/start");
        Assert.Equal(HttpStatusCode.OK, startRes.StatusCode);
        using var startDoc = await JsonDocument.ParseAsync(await startRes.Content.ReadAsStreamAsync());
        Assert.False(startDoc.RootElement.GetProperty("enabled").GetBoolean());

        var res = await client.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("any-token"));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        Assert.Equal("google_disabled", doc.RootElement.GetProperty("code").GetString());
    }

    // T8: Sec-Fetch-Site cross-site -> 403 cross_site
    [Fact]
    public async Task T8_SecFetchSiteCrossSite_Returns403CrossSite()
    {
        using var client = CreateBrowserClient(out var cookies);
        await GetStartNonceAsync(client, cookies);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/google")
        {
            Content = JsonContent.Create(new GoogleSignInRequest("some-token"))
        };
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        var res = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        Assert.Equal("cross_site", doc.RootElement.GetProperty("code").GetString());
    }

    // T9: Same token posted twice -> second is 401 token_replayed (runs on SQLite and Postgres)
    private static async Task RunT9_ReplayTokenAsync(WebApplicationFactory<Program> factory, FakeGoogleIdTokenValidator fakeValidator)
    {
        var user = await SeedUserAsync(factory, "t9_user@gmail.com", confirmed: true, linkedSub: "sub-t9");
        using var client = CreateBrowserClient(factory, out var cookies);
        var (nonce1, _) = await GetStartNonceAsync(client, cookies);

        fakeValidator.ValidateHandler = token => new GoogleIdentity(
            Subject: "sub-t9",
            Email: user.Email!,
            EmailVerified: true,
            HostedDomain: null,
            Nonce: nonce1,
            TokenId: "fixed-token-id-t9",
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30)
        );

        var res1 = await client.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("token-t9"));
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // Attempt second redemption with a fresh nonce for the session
        var (nonce2, _) = await GetStartNonceAsync(client, cookies);
        fakeValidator.ValidateHandler = token => new GoogleIdentity(
            Subject: "sub-t9",
            Email: user.Email!,
            EmailVerified: true,
            HostedDomain: null,
            Nonce: nonce2,
            TokenId: "fixed-token-id-t9", // same token ID!
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30)
        );

        var res2 = await client.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("token-t9"));
        Assert.Equal(HttpStatusCode.Unauthorized, res2.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await res2.Content.ReadAsStreamAsync());
        Assert.Equal("token_replayed", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task T9_ReplayToken_Returns401TokenReplayed()
    {
        await RunT9_ReplayTokenAsync(_factory, _fakeValidator);
    }

    [Fact]
    public async Task T9_Postgres_ReplayToken_Returns401TokenReplayed()
    {
        if (!PostgresTestDatabase.IsConfigured) return;
        using var postgres = PostgresTestDatabase.CreateIfConfigured()!;
        var fakeValidator = new FakeGoogleIdTokenValidator();
        var fakeMfa = new FakeMfaGate();
        using var factory = CreatePostgresFactory(postgres, fakeValidator, fakeMfa);
        await RunT9_ReplayTokenAsync(factory, fakeValidator);
    }

    // T10: Same token posted twice in parallel -> exactly one success (runs on SQLite and Postgres)
    private static async Task RunT10_ParallelReplayAsync(WebApplicationFactory<Program> factory, FakeGoogleIdTokenValidator fakeValidator)
    {
        var user = await SeedUserAsync(factory, "t10_user@gmail.com", confirmed: true, linkedSub: "sub-t10");
        using var client1 = CreateBrowserClient(factory, out var cookies1);
        using var client2 = CreateBrowserClient(factory, out var cookies2);

        var (nonce1, _) = await GetStartNonceAsync(client1, cookies1);
        var (nonce2, _) = await GetStartNonceAsync(client2, cookies2);

        // Client 1 will send token with nonce1, Client 2 with nonce2, both with same TokenId
        fakeValidator.ValidateHandler = token =>
        {
            var usedNonce = token.Contains("client1") ? nonce1 : nonce2;
            return new GoogleIdentity(
                Subject: "sub-t10",
                Email: user.Email!,
                EmailVerified: true,
                HostedDomain: null,
                Nonce: usedNonce,
                TokenId: "parallel-replay-token-id",
                ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30)
            );
        };

        var task1 = client1.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("token-client1"));
        var task2 = client2.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("token-client2"));

        var responses = await Task.WhenAll(task1, task2);
        var statuses = responses.Select(r => r.StatusCode).ToList();

        Assert.Contains(HttpStatusCode.OK, statuses);
        Assert.Contains(HttpStatusCode.Unauthorized, statuses);

        var failed = responses.First(r => r.StatusCode == HttpStatusCode.Unauthorized);
        using var doc = await JsonDocument.ParseAsync(await failed.Content.ReadAsStreamAsync());
        Assert.Equal("token_replayed", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task T10_ParallelReplay_ExactlyOneSucceeds()
    {
        await RunT10_ParallelReplayAsync(_factory, _fakeValidator);
    }

    [Fact]
    public async Task T10_Postgres_ParallelReplay_ExactlyOneSucceeds()
    {
        if (!PostgresTestDatabase.IsConfigured) return;
        using var postgres = PostgresTestDatabase.CreateIfConfigured()!;
        var fakeValidator = new FakeGoogleIdTokenValidator();
        var fakeMfa = new FakeMfaGate();
        using var factory = CreatePostgresFactory(postgres, fakeValidator, fakeMfa);
        await RunT10_ParallelReplayAsync(factory, fakeValidator);
    }

    // T11: Validator throws HttpRequestException -> 503 google_unavailable
    [Fact]
    public async Task T11_HttpRequestException_Returns503GoogleUnavailable()
    {
        using var client = CreateBrowserClient(out var cookies);
        await GetStartNonceAsync(client, cookies);

        _fakeValidator.ExceptionToThrow = new HttpRequestException("Google certificate servers unreachable");

        var res = await client.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("token-t11"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        Assert.Equal("google_unavailable", doc.RootElement.GetProperty("code").GetString());
    }

    // T12: Content-Type: text/plain body -> 415 unsupported media type
    [Fact]
    public async Task T12_PlainTextContentType_Returns415()
    {
        using var client = CreateBrowserClient(out var cookies);
        await GetStartNonceAsync(client, cookies);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/google")
        {
            Content = new StringContent("{\"idToken\":\"raw-string\"}", Encoding.UTF8, "text/plain")
        };
        var res = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, res.StatusCode);
    }

    // T13: Gmail address, confirmed user, not yet linked -> linked by sub, linked audit event, session
    [Fact]
    public async Task T13_GmailConfirmedUser_AutoLinksBySub()
    {
        var user = await SeedUserAsync("t13_alice@gmail.com", confirmed: true, linkedSub: null);
        using var client = CreateBrowserClient(out var cookies);
        var (nonce, _) = await GetStartNonceAsync(client, cookies);

        _fakeValidator.ValidateHandler = token => new GoogleIdentity(
            Subject: "google-alice-sub-13",
            Email: user.Email!,
            EmailVerified: true,
            HostedDomain: null,
            Nonce: nonce,
            TokenId: "tok-t13",
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30)
        );

        var res = await client.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("token-t13"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        // Verify link was created
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var reloadedUser = await userManager.FindByEmailAsync(user.Email!);
        var logins = await userManager.GetLoginsAsync(reloadedUser!);
        Assert.Contains(logins, l => l.LoginProvider == "Google" && l.ProviderKey == "google-alice-sub-13");

        // Verify audit event
        var idDb = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
        var audit = await idDb.SecurityAuditEvents.FirstOrDefaultAsync(e => e.EventType == "auth.external_login.linked" && e.UserId == user.Id);
        Assert.NotNull(audit);
    }

    // T14: Workspace address whose domain equals hd -> auto-links
    [Fact]
    public async Task T14_WorkspaceAddressMatchingHostedDomain_AutoLinks()
    {
        var user = await SeedUserAsync("t14_bob@corp.com", confirmed: true, linkedSub: null);
        using var client = CreateBrowserClient(out var cookies);
        var (nonce, _) = await GetStartNonceAsync(client, cookies);

        _fakeValidator.ValidateHandler = token => new GoogleIdentity(
            Subject: "google-bob-sub-14",
            Email: user.Email!,
            EmailVerified: true,
            HostedDomain: "corp.com",
            Nonce: nonce,
            TokenId: "tok-t14",
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30)
        );

        var res = await client.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("token-t14"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var reloadedUser = await userManager.FindByEmailAsync(user.Email!);
        var logins = await userManager.GetLoginsAsync(reloadedUser!);
        Assert.Contains(logins, l => l.LoginProvider == "Google" && l.ProviderKey == "google-bob-sub-14");
    }

    // T15: Third-party address (no hd, not Gmail) -> 409 account_linking_required, no login row
    [Fact]
    public async Task T15_ThirdPartyAddress_Returns409AccountLinkingRequired()
    {
        var user = await SeedUserAsync("t15_carol@yahoo.com", confirmed: true, linkedSub: null);
        using var client = CreateBrowserClient(out var cookies);
        var (nonce, _) = await GetStartNonceAsync(client, cookies);

        _fakeValidator.ValidateHandler = token => new GoogleIdentity(
            Subject: "google-carol-sub-15",
            Email: user.Email!,
            EmailVerified: true,
            HostedDomain: null,
            Nonce: nonce,
            TokenId: "tok-t15",
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30)
        );

        var res = await client.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("token-t15"));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        Assert.Equal("account_linking_required", doc.RootElement.GetProperty("code").GetString());

        // Assert no login row created
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var reloadedUser = await userManager.FindByEmailAsync(user.Email!);
        var logins = await userManager.GetLoginsAsync(reloadedUser!);
        Assert.Empty(logins);
    }

    // T16: No user, or user with unconfirmed email -> 404 account_not_found
    [Fact]
    public async Task T16_NoUserOrUnconfirmedEmail_Returns404AccountNotFound()
    {
        // Case A: No user exists
        using var client = CreateBrowserClient(out var cookies);
        var (nonce1, _) = await GetStartNonceAsync(client, cookies);

        _fakeValidator.ValidateHandler = token => new GoogleIdentity(
            Subject: "sub-t16-none",
            Email: "nonexistent@gmail.com",
            EmailVerified: true,
            HostedDomain: null,
            Nonce: nonce1,
            TokenId: "tok-t16-a",
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30)
        );

        var res1 = await client.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("token-t16-a"));
        Assert.Equal(HttpStatusCode.NotFound, res1.StatusCode);

        using var doc1 = await JsonDocument.ParseAsync(await res1.Content.ReadAsStreamAsync());
        Assert.Equal("account_not_found", doc1.RootElement.GetProperty("code").GetString());
        Assert.Equal("nonexistent@gmail.com", doc1.RootElement.GetProperty("email").GetString());

        // Case B: Unconfirmed user
        var unconfirmed = await SeedUserAsync("unconfirmed@gmail.com", confirmed: false);
        var (nonce2, _) = await GetStartNonceAsync(client, cookies);

        _fakeValidator.ValidateHandler = token => new GoogleIdentity(
            Subject: "sub-t16-unconf",
            Email: unconfirmed.Email!,
            EmailVerified: true,
            HostedDomain: null,
            Nonce: nonce2,
            TokenId: "tok-t16-b",
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30)
        );

        var res2 = await client.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("token-t16-b"));
        Assert.Equal(HttpStatusCode.NotFound, res2.StatusCode);
    }

    // T17: Linked sub, Google email changed -> signs in to same user (sub wins)
    [Fact]
    public async Task T17_LinkedSubEmailChanged_SignsInToSameUser()
    {
        var user = await SeedUserAsync("t17_original@gmail.com", confirmed: true, linkedSub: "sub-t17-fixed");
        using var client = CreateBrowserClient(out var cookies);
        var (nonce, _) = await GetStartNonceAsync(client, cookies);

        _fakeValidator.ValidateHandler = token => new GoogleIdentity(
            Subject: "sub-t17-fixed",
            Email: "t17_changed_email@gmail.com", // Google email changed!
            EmailVerified: true,
            HostedDomain: null,
            Nonce: nonce,
            TokenId: "tok-t17",
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30)
        );

        var res = await client.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("token-t17"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        var userElem = doc.RootElement.GetProperty("user");
        Assert.Equal(user.Id, userElem.GetProperty("id").GetString());
    }

    // T18: Two parallel first-time links, same sub -> no 500, handles race cleanly (runs on SQLite and Postgres)
    private static async Task RunT18_ParallelFirstTimeLinksAsync(WebApplicationFactory<Program> factory, FakeGoogleIdTokenValidator fakeValidator)
    {
        var user = await SeedUserAsync(factory, "t18_race@gmail.com", confirmed: true, linkedSub: null);
        using var client1 = CreateBrowserClient(factory, out var cookies1);
        using var client2 = CreateBrowserClient(factory, out var cookies2);

        var (nonce1, _) = await GetStartNonceAsync(client1, cookies1);
        var (nonce2, _) = await GetStartNonceAsync(client2, cookies2);

        fakeValidator.ValidateHandler = token =>
        {
            var is1 = token.Contains("t18-1");
            return new GoogleIdentity(
                Subject: "sub-t18-parallel",
                Email: user.Email!,
                EmailVerified: true,
                HostedDomain: null,
                Nonce: is1 ? nonce1 : nonce2,
                TokenId: is1 ? "tok-t18-1" : "tok-t18-2",
                ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30)
            );
        };

        var task1 = client1.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("t18-1"));
        var task2 = client2.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("t18-2"));

        var responses = await Task.WhenAll(task1, task2);
        foreach (var r in responses)
        {
            if (r.StatusCode == HttpStatusCode.InternalServerError)
            {
                var body = await r.Content.ReadAsStringAsync();
                Assert.Fail($"Got 500 InternalServerError: {body}");
            }
        }
        // Neither response should be 500
        Assert.DoesNotContain(responses, r => r.StatusCode == HttpStatusCode.InternalServerError);

        // Exactly one login row created
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var reloadedUser = await userManager.FindByEmailAsync(user.Email!);
        var logins = await userManager.GetLoginsAsync(reloadedUser!);
        Assert.Single(logins, l => l.LoginProvider == "Google");
    }

    [Fact]
    public async Task T18_ParallelFirstTimeLinks_HandlesRaceCleanly()
    {
        await RunT18_ParallelFirstTimeLinksAsync(_factory, _fakeValidator);
    }

    [Fact]
    public async Task T18_Postgres_ParallelFirstTimeLinks_HandlesRaceCleanly()
    {
        if (!PostgresTestDatabase.IsConfigured) return;
        using var postgres = PostgresTestDatabase.CreateIfConfigured()!;
        var fakeValidator = new FakeGoogleIdTokenValidator();
        var fakeMfa = new FakeMfaGate();
        using var factory = CreatePostgresFactory(postgres, fakeValidator, fakeMfa);
        await RunT18_ParallelFirstTimeLinksAsync(factory, fakeValidator);
    }

    // T19: User already linked to another sub -> 409 google_already_linked
    [Fact]
    public async Task T19_UserAlreadyLinkedToAnotherSub_Returns409GoogleAlreadyLinked()
    {
        var user = await SeedUserAsync("t19_user@gmail.com", confirmed: true, linkedSub: "sub-first");
        using var client = CreateBrowserClient(out var cookies);
        var (nonce, _) = await GetStartNonceAsync(client, cookies);

        _fakeValidator.ValidateHandler = token => new GoogleIdentity(
            Subject: "sub-second-different",
            Email: user.Email!,
            EmailVerified: true,
            HostedDomain: null,
            Nonce: nonce,
            TokenId: "tok-t19",
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30)
        );

        var res = await client.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("token-t19"));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        Assert.Equal("google_already_linked", doc.RootElement.GetProperty("code").GetString());
    }

    // T20: Delete account, then sign in with Google -> login row removed, returns 404 account_not_found
    [Fact]
    public async Task T20_DeleteAccount_RemovesExternalLogin_ReturnsAccountNotFound()
    {
        var user = await SeedUserAsync("t20_delete@gmail.com", confirmed: true, linkedSub: "sub-t20");

        // Delete account
        using (var scope = _factory.Services.CreateScope())
        {
            var deletionService = scope.ServiceProvider.GetRequiredService<AccountDeletionService>();
            var outcome = await deletionService.DeleteAsync(user.Id, "t20_delete@gmail.com");
            Assert.Equal(AccountDeletionStatus.Deleted, outcome.Status);

            // Assert login row was deleted
            var idDb = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            var logins = await idDb.UserLogins.Where(l => l.UserId == user.Id).ToListAsync();
            Assert.Empty(logins);

            // Assert audit event recorded
            var audit = await idDb.SecurityAuditEvents.FirstOrDefaultAsync(e => e.EventType == "auth.external_login.removed" && e.UserId == user.Id);
            Assert.NotNull(audit);
        }

        // Try signing in with Google now
        using var client = CreateBrowserClient(out var cookies);
        var (nonce, _) = await GetStartNonceAsync(client, cookies);

        _fakeValidator.ValidateHandler = token => new GoogleIdentity(
            Subject: "sub-t20",
            Email: "t20_delete@gmail.com",
            EmailVerified: true,
            HostedDomain: null,
            Nonce: nonce,
            TokenId: "tok-t20",
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30)
        );

        var res = await client.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("token-t20"));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        Assert.Equal("account_not_found", doc.RootElement.GetProperty("code").GetString());
    }

    // T21: MFA enrolled or required by policy -> returns mfa_required or mfa_enrollment_required, no refresh cookie until MFA completes
    [Theory]
    [InlineData(MfaGateResult.Challenge, "mfa_required")]
    [InlineData(MfaGateResult.Enrollment, "mfa_enrollment_required")]
    public async Task T21_MfaPolicyEnforced_RequiresFactorBOrEnrollment(MfaGateResult gateResult, string expectedStatus)
    {
        var user = await SeedUserAsync($"t21_mfa_{gateResult}@gmail.com", confirmed: true, linkedSub: $"sub-t21-{gateResult}");
        _fakeMfaGate.Decision = new MfaGateDecision(gateResult, "required", new[] { "totp" });

        using var client = CreateBrowserClient(out var cookies);
        var (nonce, _) = await GetStartNonceAsync(client, cookies);

        _fakeValidator.ValidateHandler = token => new GoogleIdentity(
            Subject: $"sub-t21-{gateResult}",
            Email: user.Email!,
            EmailVerified: true,
            HostedDomain: null,
            Nonce: nonce,
            TokenId: $"tok-t21-{gateResult}",
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30)
        );

        var res = await client.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest($"token-t21-{gateResult}"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        var status = doc.RootElement.GetProperty("status").GetString();
        Assert.Equal(expectedStatus, status);
        Assert.True(doc.RootElement.TryGetProperty("mfaToken", out _));

        // Ensure refresh cookie is NOT issued yet
        var apiUri = new Uri("https://localhost/api");
        var postCookies = cookies.GetCookies(apiUri);
        Assert.Null(postCookies["accountgo_refresh_token"]);
    }

    // T22: Invitation check on server: accepts an invitation for a different email is refused
    [Fact]
    public async Task T22_GoogleUserAcceptsInvitationForDifferentEmail_Refused()
    {
        var user = await SeedUserAsync("t22_google@gmail.com", confirmed: true, linkedSub: "sub-t22");

        // Login with Google to get access token
        using var client = CreateBrowserClient(out var cookies);
        var (nonce, _) = await GetStartNonceAsync(client, cookies);

        _fakeValidator.ValidateHandler = token => new GoogleIdentity(
            Subject: "sub-t22",
            Email: user.Email!,
            EmailVerified: true,
            HostedDomain: null,
            Nonce: nonce,
            TokenId: "tok-t22",
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30)
        );

        var res = await client.PostAsJsonAsync("/api/auth/google", new GoogleSignInRequest("token-t22"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        var token = doc.RootElement.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Create an invitation for a DIFFERENT email address
        string inviteToken;
        using (var scope = _factory.Services.CreateScope())
        {
            var apiDb = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var company = new Core.Domain.Company
            {
                Name = "T22 Workspace",
                Slug = "t22-workspace",
                Plan = "community"
            };
            apiDb.Companies.Add(company);
            await apiDb.SaveChangesAsync();

            var rawToken = Guid.NewGuid().ToString("N");
            var invitation = new Core.Domain.CompanyInvitation
            {
                CompanyId = company.Id,
                Email = "someone_else@example.com",
                TokenHash = WorkspaceInvitationService.HashToken(rawToken),
                Role = Core.Domain.CompanyMemberRole.Member,
                InvitedByUserId = user.Id,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };
            apiDb.CompanyInvitations.Add(invitation);
            await apiDb.SaveChangesAsync();
            inviteToken = rawToken;
        }

        // Accept invitation with user's token
        var acceptRes = await client.PostAsync($"/api/invitations/{inviteToken}/accept", null);
        // Server rejects because logged in user email != invited email
        Assert.False(acceptRes.IsSuccessStatusCode);
    }
}
