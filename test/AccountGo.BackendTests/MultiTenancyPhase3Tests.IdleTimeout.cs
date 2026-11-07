#nullable disable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests
{
    // Automatic sign-out: idle limit = strictest workspace setting, 12-hour hard limit from sign-in.
    public partial class MultiTenancyPhase3Tests
    {
        private const string SessionPolicy = "/api/organizations/current/session-policy";

        [Fact]
        public async Task Refresh_UsesDefaultIdleTimeout_AndTwelveHourLimit()
        {
            var raw = "test-refresh-" + Guid.NewGuid().ToString("N");
            var familyId = AddSession(raw);
            using var browser = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

            var before = DateTime.UtcNow;
            var response = await browser.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = raw });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await Json(response);
            Assert.Equal(SessionTimeoutPolicy.DefaultIdleMinutes * 60, body.GetProperty("idleTimeoutSeconds").GetInt32());

            var issued = ActiveToken(familyId);
            // Idle limit plus slack for the browser's 5-minute activity refresh.
            AssertNear(before + TimeSpan.FromMinutes(60) + SessionTimeoutPolicy.ServerSlack, issued.ExpiresAt);
            // Rows from before this change get their 12 hours from the next refresh.
            AssertNear(before.AddHours(12), issued.AbsoluteExpiresAt.Value);
            AssertNear(issued.AbsoluteExpiresAt.Value, body.GetProperty("sessionExpiresAt").GetDateTime().ToUniversalTime());
            Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("accountgo_refresh_token=", StringComparison.Ordinal) && c.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Refresh_KeepsTheSignInsTwelveHourLimit_AcrossRotations()
        {
            var raw = "test-refresh-" + Guid.NewGuid().ToString("N");
            var familyId = AddSession(raw);
            var limit = DateTime.UtcNow.AddHours(3);
            SetToken(raw, t => t.AbsoluteExpiresAt = limit);
            using var browser = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

            var next = await Refresh(browser, raw);
            Assert.NotNull(next);
            AssertNear(limit, ActiveToken(familyId).AbsoluteExpiresAt.Value);

            // Grace sibling (second tab) keeps the same limit.
            Assert.NotNull(await Refresh(browser, raw));
            Assert.All(ActiveTokens(familyId), t => AssertNear(limit, t.AbsoluteExpiresAt.Value));
        }

        [Fact]
        public async Task Refresh_IsRefused_AfterIdleLimit_OrTwelveHourLimit()
        {
            using var browser = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

            var idle = "test-refresh-" + Guid.NewGuid().ToString("N");
            AddSession(idle);
            SetToken(idle, t => t.ExpiresAt = DateTime.UtcNow.AddSeconds(-1));
            Assert.Null(await Refresh(browser, idle));

            var hardLimit = "test-refresh-" + Guid.NewGuid().ToString("N");
            AddSession(hardLimit);
            SetToken(hardLimit, t => t.AbsoluteExpiresAt = DateTime.UtcNow.AddSeconds(-1));
            Assert.Null(await Refresh(browser, hardLimit));
        }

        [Fact]
        public async Task GraceReuse_IsRefused_AfterTwelveHourLimit()
        {
            var raw = "test-refresh-" + Guid.NewGuid().ToString("N");
            var familyId = AddSession(raw);
            using var browser = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            Assert.NotNull(await Refresh(browser, raw));

            using (var scope = _factory.Services.CreateScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
                foreach (var t in identity.RefreshTokens.Where(t => t.FamilyId == familyId))
                    t.AbsoluteExpiresAt = DateTime.UtcNow.AddSeconds(-1);
                await identity.SaveChangesAsync();
            }

            // Presenting the rotated parent inside the 5-minute grace window must not mint a new token.
            Assert.Null(await Refresh(browser, raw));
        }

        [Fact]
        public async Task IdleTimeout_IsTheStrictestOfTheUsersWorkspaces_IgnoringDeletedOnes()
        {
            var created = await Send(HttpMethod.Post, "/api/organizations", new { name = "Strict Client", slug = "strict-client" }, token: _otherToken, scoped: false);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var strictCompanyId = int.Parse((await Json(created)).GetProperty("id").GetString());
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                db.CompanyMembers.Add(new CompanyMember
                {
                    CompanyId = strictCompanyId,
                    UserId = _ownerId,
                    Role = CompanyMemberRole.Member,
                    Status = CompanyMemberStatus.Active,
                    JoinedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Put, SessionPolicy, new { idleTimeoutMinutes = 120 })).StatusCode);
            var strict = await Send(HttpMethod.Put, SessionPolicy, new { idleTimeoutMinutes = 30 }, token: _otherToken, scoped: false,
                extraHeaders: new System.Collections.Generic.Dictionary<string, string> { ["X-AccountGo-Org"] = "strict-client" });
            Assert.Equal(HttpStatusCode.OK, strict.StatusCode);

            Assert.Equal(30 * 60, await RefreshIdleSeconds());

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                db.Companies.Single(c => c.Id == strictCompanyId).DeletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            Assert.Equal(120 * 60, await RefreshIdleSeconds());
        }

        [Fact]
        public async Task SessionPolicy_AdminsChooseAnAllowedValue_AndTheChangeIsAudited()
        {
            var initial = await Json(await Send(HttpMethod.Get, SessionPolicy));
            Assert.Equal(SessionTimeoutPolicy.DefaultIdleMinutes, initial.GetProperty("idleTimeoutMinutes").GetInt32());
            Assert.Equal(12, initial.GetProperty("absoluteSessionHours").GetInt32());

            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, SessionPolicy, new { idleTimeoutMinutes = 45 })).StatusCode);

            var saved = await Send(HttpMethod.Put, SessionPolicy, new { idleTimeoutMinutes = 15 });
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal(15, (await Json(saved)).GetProperty("idleTimeoutMinutes").GetInt32());

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var change = db.RecordChanges
                    .IgnoreQueryFilters()
                    .Where(c => c.CompanyId == _companyId && c.EntityType == "CompanySetting")
                    .AsEnumerable()
                    .LastOrDefault(c => c.ChangesJson.Contains("IdleTimeoutMinutes", StringComparison.Ordinal));
                Assert.NotNull(change);
                Assert.Equal(_ownerId, change.ActorUserId);
                Assert.Contains("60", change.ChangesJson);
                Assert.Contains("15", change.ChangesJson);
            }

            // A plain member can read the policy but not change it.
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                db.CompanyMembers.Add(new CompanyMember
                {
                    CompanyId = _companyId,
                    UserId = _otherId,
                    Role = CompanyMemberRole.Member,
                    Status = CompanyMemberStatus.Active,
                    JoinedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, SessionPolicy, token: _otherToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Put, SessionPolicy, new { idleTimeoutMinutes = 240 }, token: _otherToken)).StatusCode);
        }

        [Fact]
        public async Task Logout_WithoutAccessToken_RevokesTheWholeSignIn_AndRecordsTimeout()
        {
            var raw = "test-refresh-" + Guid.NewGuid().ToString("N");
            var familyId = AddSession(raw);
            using var browser = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            var current = await Refresh(browser, raw);
            Assert.NotNull(await Refresh(browser, raw)); // second tab's grace sibling
            Assert.Equal(2, ActiveTokens(familyId).Length);

            using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout?reason=idle"))
            {
                request.Headers.Add("Cookie", "accountgo_refresh_token=" + Uri.EscapeDataString(current));
                var response = await browser.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            Assert.Empty(ActiveTokens(familyId));
            using var scope = _factory.Services.CreateScope();
            var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            var audit = identity.SecurityAuditEvents.Where(e => e.EventType == "auth.session.timeout").ToList();
            Assert.Single(audit);
            Assert.Equal(_ownerId, audit[0].UserId);
            Assert.Contains("idle", audit[0].MetadataJson);
        }

        private async Task<int> RefreshIdleSeconds()
        {
            var raw = "test-refresh-" + Guid.NewGuid().ToString("N");
            AddSession(raw);
            using var browser = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            var response = await browser.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = raw });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await Json(response)).GetProperty("idleTimeoutSeconds").GetInt32();
        }

        private void SetToken(string rawToken, Action<RefreshTokenSession> change)
        {
            using var scope = _factory.Services.CreateScope();
            var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            change(identity.RefreshTokens.Single(t => t.TokenHash == Hash(rawToken)));
            identity.SaveChanges();
        }

        private RefreshTokenSession ActiveToken(string familyId) => ActiveTokens(familyId).Single();

        private RefreshTokenSession[] ActiveTokens(string familyId)
        {
            using var scope = _factory.Services.CreateScope();
            var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            return identity.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToArray();
        }

        private static void AssertNear(DateTime expected, DateTime actual) =>
            Assert.True(Math.Abs((expected - actual).TotalSeconds) < 30, $"expected ~{expected:O}, got {actual:O}");
    }
}
