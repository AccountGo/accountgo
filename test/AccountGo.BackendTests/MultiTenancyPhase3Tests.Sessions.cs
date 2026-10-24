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
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests
{
    // Refresh tokens are single-use: each refresh replaces the token, and an old token coming back is treated as theft.
    public partial class MultiTenancyPhase3Tests
    {
        [Fact]
        public async Task Refresh_ARecentlyRotatedTokenIsAcceptedOnce_TheUnreceivedReplacementIsRevoked()
        {
            var first = "test-refresh-" + Guid.NewGuid().ToString("N");
            AddSession(first);
            using var browser = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

            var second = await Refresh(browser, first);
            Assert.NotNull(second);

            // The response carrying the second token was lost, so the browser presents the first again: within the grace
            // period the session carries on, and the second token, which the browser never received, is revoked.
            var third = await Refresh(browser, first);
            Assert.NotNull(third);
            var fourth = await Refresh(browser, third);
            Assert.NotNull(fourth);

            // Only someone else could hold the second token, so presenting it still ends every session.
            Assert.Null(await Refresh(browser, second));
            Assert.Null(await Refresh(browser, fourth));
            AssertNoActiveSessions();
        }

        [Fact]
        public async Task Refresh_ATokenRotatedLongAgo_EndsEverySession()
        {
            var first = "test-refresh-" + Guid.NewGuid().ToString("N");
            AddSession(first);
            using var browser = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            var second = await Refresh(browser, first);
            Assert.NotNull(second);

            using (var scope = _factory.Services.CreateScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
                identity.RefreshTokens.Single(t => t.TokenHash == Hash(first)).RevokedAt = DateTime.UtcNow.AddMinutes(-10);
                await identity.SaveChangesAsync();
            }
            Assert.Null(await Refresh(browser, first));
            Assert.Null(await Refresh(browser, second));
            AssertNoActiveSessions();
        }

        private void AssertNoActiveSessions()
        {
            using var scope = _factory.Services.CreateScope();
            var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            Assert.DoesNotContain(identity.RefreshTokens.Where(t => t.UserId == _ownerId), t => t.RevokedAt == null);
        }

        private void AddSession(string rawToken)
        {
            using var scope = _factory.Services.CreateScope();
            var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            identity.RefreshTokens.Add(new RefreshTokenSession
            {
                UserId = _ownerId,
                TokenHash = Hash(rawToken),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
            });
            identity.SaveChanges();
        }

        /// <summary>Refreshes with a raw token; returns the new raw token from the cookie, or null when refused.</summary>
        private static async Task<string> Refresh(HttpClient browser, string rawToken)
        {
            var response = await browser.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = rawToken });
            if (!response.IsSuccessStatusCode)
                return null;
            var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("accountgo_refresh_token=", StringComparison.Ordinal));
            return WebUtility.UrlDecode(cookie.Substring("accountgo_refresh_token=".Length).Split(';')[0]);
        }

        private static string Hash(string rawToken) =>
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }
}
