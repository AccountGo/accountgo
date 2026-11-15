#nullable disable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Api.Data;
using Api.Service;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace AccountGo.BackendTests
{
    // Refresh tokens rotate per use. Concurrent reuse within the grace window mints family siblings
    // (bounded); reuse outside grace revokes only that family.
    public abstract partial class MultiTenancyPhase3TestBase
    {

        /// <summary>Returns the FamilyId assigned to the new session.</summary>
        protected string AddSession(string rawToken, string familyId = null)
        {
            familyId ??= Guid.NewGuid().ToString("N");
            using var scope = _factory.Services.CreateScope();
            var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            identity.RefreshTokens.Add(new RefreshTokenSession
            {
                UserId = _ownerId,
                FamilyId = familyId,
                TokenHash = Hash(rawToken),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                GraceReuseCount = 0,
            });
            identity.SaveChanges();
            return familyId;
        }

        /// <summary>Refreshes with a raw token; returns the new raw token from the cookie, or null when refused.</summary>
        protected static async Task<string> Refresh(HttpClient browser, string rawToken)
        {
            var response = await browser.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = rawToken });
            if (!response.IsSuccessStatusCode)
                return null;
            var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("accountgo_refresh_token=", StringComparison.Ordinal));
            return WebUtility.UrlDecode(cookie.Substring("accountgo_refresh_token=".Length).Split(';')[0]);
        }

        protected static string Hash(string rawToken) =>
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }

    public sealed class MultiTenancyPhase3SessionsTests : MultiTenancyPhase3TestBase
    {
        public MultiTenancyPhase3SessionsTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public async Task Refresh_GraceReuse_KeepsReplacement_AndIssuesSibling()
        {
            var first = "test-refresh-" + Guid.NewGuid().ToString("N");
            AddSession(first);
            using var browser = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

            var second = await Refresh(browser, first);
            Assert.NotNull(second);

            // Lost response / concurrent tab: present first again within grace — sibling issued; second stays live.
            var third = await Refresh(browser, first);
            Assert.NotNull(third);
            Assert.NotEqual(second, third);

            Assert.NotNull(await Refresh(browser, second));
            Assert.NotNull(await Refresh(browser, third));
        }

        [Fact]
        public async Task Refresh_StaleCookieOrder_T1StillWorksAfterGraceSibling()
        {
            var t0 = "test-refresh-" + Guid.NewGuid().ToString("N");
            AddSession(t0);
            using var browser = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

            var t1 = await Refresh(browser, t0);
            Assert.NotNull(t1);

            // Peer refresh with T0 mints sibling T2; browser still holding T1 must refresh fine.
            var t2 = await Refresh(browser, t0);
            Assert.NotNull(t2);

            var t1Next = await Refresh(browser, t1);
            Assert.NotNull(t1Next);
        }

        [Fact]
        public async Task Refresh_TwoConcurrentRefreshesWithSameToken_NoFamilyRevoked()
        {
            var t0 = "test-refresh-" + Guid.NewGuid().ToString("N");
            var familyId = AddSession(t0);
            using var browser = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

            var taskA = Refresh(browser, t0);
            var taskB = Refresh(browser, t0);
            var results = await Task.WhenAll(taskA, taskB);

            Assert.Contains(results, r => r != null);
            // At least one active token remains in the family (neither path wiped the family).
            using var scope = _factory.Services.CreateScope();
            var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            Assert.Contains(
                identity.RefreshTokens.Where(t => t.FamilyId == familyId),
                t => t.RevokedAt == null);
        }

        [Fact]
        public async Task Refresh_ReuseOutsideGrace_RevokesOnlyThatFamily()
        {
            var deviceA = "test-refresh-a-" + Guid.NewGuid().ToString("N");
            var deviceB = "test-refresh-b-" + Guid.NewGuid().ToString("N");
            var familyA = AddSession(deviceA);
            var familyB = AddSession(deviceB);
            Assert.NotEqual(familyA, familyB);

            using var browser = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            var a2 = await Refresh(browser, deviceA);
            Assert.NotNull(a2);

            using (var scope = _factory.Services.CreateScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
                identity.RefreshTokens.Single(t => t.TokenHash == Hash(deviceA)).RevokedAt = DateTime.UtcNow.AddMinutes(-10);
                await identity.SaveChangesAsync();
            }

            Assert.Null(await Refresh(browser, deviceA));
            Assert.Null(await Refresh(browser, a2));

            // Second device's family stays active.
            Assert.NotNull(await Refresh(browser, deviceB));
            using (var scope = _factory.Services.CreateScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
                Assert.DoesNotContain(
                    identity.RefreshTokens.Where(t => t.FamilyId == familyA),
                    t => t.RevokedAt == null);
                Assert.Contains(
                    identity.RefreshTokens.Where(t => t.FamilyId == familyB),
                    t => t.RevokedAt == null);
            }
        }

        [Fact]
        public async Task Refresh_GraceReuseOverBound_RevokesFamily()
        {
            var t0 = "test-refresh-" + Guid.NewGuid().ToString("N");
            var familyId = AddSession(t0);
            using var browser = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

            Assert.NotNull(await Refresh(browser, t0)); // primary rotation

            // Three grace siblings allowed.
            Assert.NotNull(await Refresh(browser, t0));
            Assert.NotNull(await Refresh(browser, t0));
            Assert.NotNull(await Refresh(browser, t0));

            // Fourth grace reuse exceeds bound → family revoked.
            Assert.Null(await Refresh(browser, t0));

            using var scope = _factory.Services.CreateScope();
            var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            var parent = identity.RefreshTokens.Single(t => t.TokenHash == Hash(t0));
            Assert.Equal(3, parent.GraceReuseCount);
            Assert.DoesNotContain(
                identity.RefreshTokens.Where(t => t.FamilyId == familyId),
                t => t.RevokedAt == null);
        }

        [Fact]
        public async Task Logout_RevokesFamily_AndRejectsGraceRefreshWithRotatedAncestor()
        {
            var t0 = "test-refresh-" + Guid.NewGuid().ToString("N");
            var familyId = AddSession(t0);
            using var browser = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

            var t1 = await Refresh(browser, t0);
            Assert.NotNull(t1);

            using (var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout"))
            {
                logout.Headers.Add("Cookie", "accountgo_refresh_token=" + Uri.EscapeDataString(t1));
                var response = await browser.SendAsync(logout);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            // Rotated ancestor t0 is still within the 5-minute window, but the family was revoked on logout.
            Assert.Null(await Refresh(browser, t0));
            Assert.Null(await Refresh(browser, t1));

            using var scope = _factory.Services.CreateScope();
            var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            Assert.DoesNotContain(
                identity.RefreshTokens.Where(t => t.FamilyId == familyId),
                t => t.RevokedAt == null);
        }

        [Fact]
        public async Task Refresh_RejectsCliChannelTokens_OnWebRefreshEndpoint()
        {
            var cliRaw = "test-cli-refresh-" + Guid.NewGuid().ToString("N");
            var familyId = Guid.NewGuid().ToString("N");
            using (var scope = _factory.Services.CreateScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
                identity.RefreshTokens.Add(new RefreshTokenSession
                {
                    UserId = _ownerId,
                    FamilyId = familyId,
                    TokenHash = Hash(cliRaw),
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                    Channel = RefreshTokenChannels.Cli,
                    ClientId = "accountgo-cli",
                    Scope = "offline_access api",
                });
                await identity.SaveChangesAsync();
            }

            using var browser = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            Assert.Null(await Refresh(browser, cliRaw));

            // Also reject if the CLI token was already rotated within the grace window.
            using (var scope = _factory.Services.CreateScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
                var row = identity.RefreshTokens.Single(t => t.TokenHash == Hash(cliRaw));
                row.RevokedAt = DateTime.UtcNow;
                row.ReplacedByTokenHash = Hash("cli-replacement");
                await identity.SaveChangesAsync();
            }

            Assert.Null(await Refresh(browser, cliRaw));
        }

        [Fact]
        public async Task ConcurrentRefresh_PausedBeforeInsert_CannotSurviveLogout()
        {
            var t0 = "test-refresh-" + Guid.NewGuid().ToString("N");
            var familyId = AddSession(t0);
            using var browserA = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            using var browserB = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

            // Rotate once so t0 is a rotated ancestor within the grace window and t1 is the active child.
            var t1 = await Refresh(browserA, t0);
            Assert.NotNull(t1);

            var gate = _factory.Services.GetRequiredService<SessionRefreshTestGate>();
            var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            gate.BeforeInsertAsync = async currentFamilyId =>
            {
                if (currentFamilyId == familyId)
                {
                    paused.TrySetResult();
                    await resume.Task;
                }
            };

            Task<string> inFlightRefresh = null;
            try
            {
                // Start a grace refresh on t0 that passes the initial check and pauses right before inserting a sibling.
                inFlightRefresh = Refresh(browserA, t0);
                await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));

                // While the refresh is paused, complete logout on the family.
                using (var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout"))
                {
                    logout.Headers.Add("Cookie", "accountgo_refresh_token=" + Uri.EscapeDataString(t1));
                    var response = await browserB.SendAsync(logout);
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                }

                // Resume the paused refresh; it must detect the family revocation and refuse to mint a sibling.
                resume.TrySetResult();
                var siblingToken = await inFlightRefresh;
                Assert.Null(siblingToken);
            }
            finally
            {
                gate.Reset();
                resume.TrySetResult();
                if (inFlightRefresh != null)
                {
                    await inFlightRefresh;
                }
            }

            using var scope = _factory.Services.CreateScope();
            var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            Assert.DoesNotContain(
                identity.RefreshTokens.Where(t => t.FamilyId == familyId),
                t => t.RevokedAt == null);
        }

        [Fact]
        public async Task ConcurrentActiveRotation_PausedBeforeInsert_CannotSurviveLogout()
        {
            var t0 = "test-refresh-" + Guid.NewGuid().ToString("N");
            var familyId = AddSession(t0);
            using var browserA = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            using var browserB = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

            var gate = _factory.Services.GetRequiredService<SessionRefreshTestGate>();
            var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            gate.BeforeInsertAsync = async currentFamilyId =>
            {
                if (currentFamilyId == familyId)
                {
                    paused.TrySetResult();
                    await resume.Task;
                }
            };

            Task<string> inFlightRefresh = null;
            try
            {
                inFlightRefresh = Refresh(browserA, t0);
                await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));

                using (var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout"))
                {
                    logout.Headers.Add("Cookie", "accountgo_refresh_token=" + Uri.EscapeDataString(t0));
                    var response = await browserB.SendAsync(logout);
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                }

                resume.TrySetResult();
                var rotatedToken = await inFlightRefresh;
                Assert.Null(rotatedToken);
            }
            finally
            {
                gate.Reset();
                resume.TrySetResult();
                if (inFlightRefresh != null)
                {
                    await inFlightRefresh;
                }
            }

            using var scope = _factory.Services.CreateScope();
            var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            Assert.DoesNotContain(
                identity.RefreshTokens.Where(t => t.FamilyId == familyId),
                t => t.RevokedAt == null);
        }
    }
}
