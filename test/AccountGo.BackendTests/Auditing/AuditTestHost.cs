// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Api.Data;
using Api.Service;
using Core.Domain;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Services.Entitlements;
using Xunit;

namespace AccountGo.BackendTests.Auditing
{
    /// <summary>
    /// Shared HTTP host for the security-event and activity API tests: SQLite, captured email and logs,
    /// a fixed client IP, Server-Timing on, and per-workspace entitlement overrides.
    /// </summary>
    public sealed class AuditTestHost : IAsyncLifetime
    {
        public const string ClientIp = "203.0.113.7";
        public const string CommunityPlan = PlanKeys.Community;
        public const string HostedPlan = "pro";

        private const string SecretKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private static readonly TimeSpan EventWaitTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan EventPollInterval = TimeSpan.FromMilliseconds(50);

        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), "accountgo_audit_" + Guid.NewGuid().ToString("N") + ".db");
        private HttpClient _client = null!;

        public WebApplicationFactory<Program> Factory { get; private set; } = null!;
        public CapturingEmailSender Emails { get; } = new();
        public CapturingLoggerProvider Logs { get; } = new();
        public EntitlementOverrides Entitlements { get; } = new();

        public Task InitializeAsync()
        {
            Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("DatabaseProvider", "Sqlite");
                builder.UseSetting("ConnectionStrings:SqliteConnection", "Data Source=" + _dbPath);
                builder.UseSetting("SECRET_KEY", SecretKey);
                builder.UseSetting("PUBLIC_WEB_URL", "https://workspace.example");
                builder.UseSetting("PUBLIC_API_URL", "https://api.example");
                builder.UseSetting("SERVER_TIMING_ENABLED", "true");
                builder.ConfigureLogging(logging => logging.AddProvider(Logs));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IEmailSender>();
                    services.AddSingleton<IEmailSender>(Emails);
                    services.AddSingleton(Entitlements);
                    services.RemoveAll<IEntitlementService>();
                    services.AddScoped<IEntitlementService>(sp => new OverridableEntitlementService(
                        ActivatorUtilities.CreateInstance<CommunityEntitlementService>(sp),
                        sp.GetRequiredService<EntitlementOverrides>()));
                    services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter>(new FixedRemoteIpStartupFilter(IPAddress.Parse(ClientIp)));
                });
            });
            _client = Factory.CreateClient();
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            _client?.Dispose();
            Factory?.Dispose();
            foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            return Task.CompletedTask;
        }

        // ---- identity and workspace setup -------------------------------------------------------------

        public sealed record UserSession(string Token, string Id, string Email);

        public async Task<UserSession> CreateUser(string email, bool isSystemAdministrator = false)
        {
            using var scope = Factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await userManager.CreateAsync(user)).Succeeded, email);
            if (isSystemAdministrator)
            {
                if (!await roleManager.RoleExistsAsync("SystemAdministrators"))
                    await roleManager.CreateAsync(new IdentityRole("SystemAdministrators"));
                await userManager.AddToRoleAsync(user, "SystemAdministrators");
            }
            var token = await scope.ServiceProvider.GetRequiredService<IAuthenticationService>().CreateToken(user, true);
            return new UserSession(token.AccessToken, user.Id, email);
        }

        public async Task<int> CreateWorkspace(string token, string name, string slug)
        {
            var response = await Send(HttpMethod.Post, "/api/organizations", new { name, slug }, token, scoped: false);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return int.Parse((await Json(response)).GetProperty("id").GetString()!, CultureInfo.InvariantCulture);
        }

        public async Task SetFunctionalRoles(int companyId, string userId, params string[] roleNames)
        {
            using var scope = Factory.Services.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var impersonation = tenant.Impersonate(companyId, role: CompanyMemberRole.Owner);
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var member = db.CompanyMembers.Include(m => m.SecurityRoles)
                .Single(m => m.CompanyId == companyId && m.UserId == userId);
            db.CompanyMemberSecurityRoles.RemoveRange(member.SecurityRoles);
            var roleIds = db.SecurityRoles.Where(r => roleNames.Contains(r.Name)).Select(r => r.Id).ToList();
            foreach (var roleId in roleIds)
                db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole { CompanyMemberId = member.Id, SecurityRoleId = roleId });
            await db.SaveChangesAsync();
        }

        public async Task<int> AddMember(int companyId, string userId, CompanyMemberRole role, params string[] roleNames)
        {
            using var scope = Factory.Services.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var impersonation = tenant.Impersonate(companyId, role: CompanyMemberRole.Owner);
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var member = new CompanyMember
            {
                CompanyId = companyId,
                UserId = userId,
                Role = role,
                Status = CompanyMemberStatus.Active,
                JoinedAt = DateTime.UtcNow
            };
            db.CompanyMembers.Add(member);
            await db.SaveChangesAsync();
            var roleIds = db.SecurityRoles.Where(r => roleNames.Contains(r.Name)).Select(r => r.Id).ToList();
            foreach (var roleId in roleIds)
                db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole { CompanyMemberId = member.Id, SecurityRoleId = roleId });
            if (roleIds.Count > 0)
                await db.SaveChangesAsync();
            return member.Id;
        }

        public int SecurityRoleId(string roleName)
        {
            using var scope = Factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ApiDbContext>().SecurityRoles.Single(r => r.Name == roleName).Id;
        }

        public int MemberId(int companyId, string userId)
        {
            using var scope = Factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ApiDbContext>().CompanyMembers
                .IgnoreQueryFilters()
                .Single(m => m.CompanyId == companyId && m.UserId == userId).Id;
        }

        // ---- HTTP -------------------------------------------------------------------------------------

        public async Task<HttpResponseMessage> Send(
            HttpMethod method,
            string path,
            object? body = null,
            string? token = null,
            string? org = null,
            bool scoped = true)
        {
            using var request = new HttpRequestMessage(method, path);
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (scoped)
                request.Headers.Add("X-AccountGo-Org", org ?? "default-org");
            if (body != null)
                request.Content = JsonContent.Create(body);
            return await _client.SendAsync(request);
        }

        public static async Task<JsonElement> Json(HttpResponseMessage response)
        {
            var text = await response.Content.ReadAsStringAsync();
            return JsonDocument.Parse(text).RootElement.Clone();
        }

        public static string RequestId(HttpResponseMessage response) =>
            response.Headers.GetValues("X-Request-Id").Single();

        /// <summary>Parses the db query count from <c>Server-Timing: db;dur=3;desc="N queries"</c>.</summary>
        public static int ServerTimingQueryCount(HttpResponseMessage response)
        {
            var header = string.Join(", ", response.Headers.GetValues("Server-Timing"));
            var match = Regex.Match(header, "desc=\"(\\d+) quer(?:y|ies)\"");
            Assert.True(match.Success, "Server-Timing header had no query count: " + header);
            return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        // ---- OTP --------------------------------------------------------------------------------------

        /// <summary>Requests an OTP for an existing user and returns the code captured from the delivered email.</summary>
        public async Task<string> RequestOtpCode(string email)
        {
            var before = Emails.Messages.Count;
            var response = await Send(HttpMethod.Post, "/api/auth/send-otp", new { email }, scoped: false);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var message = Emails.Messages.Skip(before).Last(m => m.Subject.StartsWith("AccountGo Sign-In Code: ", StringComparison.Ordinal));
            return message.Subject["AccountGo Sign-In Code: ".Length..].Trim();
        }

        public Task<HttpResponseMessage> VerifyOtp(string email, string otp) =>
            Send(HttpMethod.Post, "/api/auth/verify-otp", new { email, otp }, scoped: false);

        // ---- security event reads ---------------------------------------------------------------------

        public List<SecurityAuditEvent> ReadEvents(Func<SecurityAuditEvent, bool> predicate)
        {
            using var scope = Factory.Services.CreateScope();
            var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            return identity.SecurityAuditEvents.AsNoTracking().AsEnumerable().Where(predicate).OrderBy(e => e.Id).ToList();
        }

        /// <summary>Polls until at least <paramref name="minimum"/> matching events exist (events may be written after the response).</summary>
        public async Task<List<SecurityAuditEvent>> WaitForEvents(Func<SecurityAuditEvent, bool> predicate, int minimum = 1)
        {
            var deadline = DateTime.UtcNow + EventWaitTimeout;
            List<SecurityAuditEvent> found;
            do
            {
                found = ReadEvents(predicate);
                if (found.Count >= minimum)
                    return found;
                await Task.Delay(EventPollInterval);
            }
            while (DateTime.UtcNow < deadline);
            return found;
        }

        /// <summary>Lets any trailing fire-and-forget writes settle, then returns the matching events.</summary>
        public async Task<List<SecurityAuditEvent>> SettledEvents(Func<SecurityAuditEvent, bool> predicate, int expectedMinimum = 0)
        {
            if (expectedMinimum > 0)
                await WaitForEvents(predicate, expectedMinimum);
            await Task.Delay(TimeSpan.FromMilliseconds(400));
            return ReadEvents(predicate);
        }

        public async Task InsertSecurityEvents(IEnumerable<SecurityAuditEvent> events)
        {
            using var scope = Factory.Services.CreateScope();
            var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            identity.SecurityAuditEvents.AddRange(events);
            await identity.SaveChangesAsync();
        }

        public async Task InsertRecordChanges(int companyId, int count, DateTime? firstOccurredAt = null, TimeSpan? spacing = null, string entityType = "Item", int entityId = 0, string? actorUserId = null)
        {
            using var scope = Factory.Services.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var impersonation = tenant.Impersonate(companyId, role: CompanyMemberRole.Owner);
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var start = firstOccurredAt ?? DateTime.UtcNow.AddHours(-count);
            var step = spacing ?? TimeSpan.FromMinutes(1);
            for (var index = 0; index < count; index++)
            {
                db.RecordChanges.Add(new Core.Domain.Auditing.RecordChange
                {
                    CompanyId = companyId,
                    EntityType = entityType,
                    EntityId = entityId == 0 ? index + 1 : entityId,
                    Action = "Modified",
                    ChangesJson = "{\"Name\":{\"old\":\"a\",\"new\":\"b\"}}",
                    ActorUserId = actorUserId,
                    ActorKind = "user",
                    CorrelationId = "seed-" + index.ToString(CultureInfo.InvariantCulture),
                    OccurredAt = start + step * index
                });
            }
            await db.SaveChangesAsync();
        }

        // ---- test doubles -----------------------------------------------------------------------------

        public sealed class CapturingEmailSender : IEmailSender
        {
            public List<EmailMessage> Messages { get; } = new();

            public Task<string?> SendAsync(EmailMessage message, System.Threading.CancellationToken ct = default)
            {
                lock (Messages) Messages.Add(message);
                return Task.FromResult<string?>("captured-" + Messages.Count);
            }
        }

        public sealed record LogEntry(string Category, LogLevel Level, string Message);

        public sealed class CapturingLoggerProvider : ILoggerProvider
        {
            private readonly ConcurrentQueue<LogEntry> _entries = new();

            public IReadOnlyList<LogEntry> Entries => _entries.ToArray();

            public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

            public void Dispose()
            {
            }

            private sealed class CapturingLogger : ILogger
            {
                private readonly string _category;
                private readonly ConcurrentQueue<LogEntry> _entries;

                public CapturingLogger(string category, ConcurrentQueue<LogEntry> entries)
                {
                    _category = category;
                    _entries = entries;
                }

                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

                public bool IsEnabled(LogLevel logLevel) => true;

                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                {
                    var message = formatter(state, exception);
                    if (exception != null)
                        message += " " + exception;
                    _entries.Enqueue(new LogEntry(_category, logLevel, message));
                }
            }
        }

        /// <summary>Per-workspace entitlement snapshot overrides; workspaces without an override stay on the community engine.</summary>
        public sealed class EntitlementOverrides
        {
            private readonly ConcurrentDictionary<int, (string Plan, IReadOnlySet<string> Features)> _overrides = new();

            public void UsePlan(int companyId, string plan, params string[] features) =>
                _overrides[companyId] = (plan, new HashSet<string>(features, StringComparer.Ordinal));

            public void Clear(int companyId) => _overrides.TryRemove(companyId, out _);

            public bool TryGet(int companyId, out (string Plan, IReadOnlySet<string> Features) value) =>
                _overrides.TryGetValue(companyId, out value);
        }

        private sealed class OverridableEntitlementService : IEntitlementService
        {
            private readonly IEntitlementService _inner;
            private readonly EntitlementOverrides _overrides;

            public OverridableEntitlementService(IEntitlementService inner, EntitlementOverrides overrides)
            {
                _inner = inner;
                _overrides = overrides;
            }

            public EntitlementsOptions Options => _inner.Options;

            public EntitlementSnapshot Get(int companyId)
            {
                var snapshot = _inner.Get(companyId);
                if (!_overrides.TryGet(companyId, out var overridden))
                    return snapshot;
                return snapshot with
                {
                    Plan = overridden.Plan,
                    EffectivePlan = overridden.Plan,
                    Features = overridden.Features
                };
            }

            public EntitlementDecision Evaluate(int companyId, EntitlementAction action) => _inner.Evaluate(companyId, action);
            public void Ensure(int companyId, EntitlementAction action) => _inner.Ensure(companyId, action);
            public EntitlementDecision EvaluateForUser(string userId, UserAction action) => _inner.EvaluateForUser(userId, action);
            public void ClearPolicyMemo(int companyId) => _inner.ClearPolicyMemo(companyId);
            public bool TryGetResolvedPolicyVersion(int companyId, out string version) => _inner.TryGetResolvedPolicyVersion(companyId, out version);
            public bool TryGetResolvedTier(int companyId, out string tier) => _inner.TryGetResolvedTier(companyId, out tier);
            public void EnsureCanCreate(int companyId, MeteredResource resource, int count = 1) => _inner.EnsureCanCreate(companyId, resource, count);
            public void EnsureCanCreate(MeteredResource resource, int count = 1) => _inner.EnsureCanCreate(resource, count);
            public void Record(MeteredResource resource, int? entityId) => _inner.Record(resource, entityId);
            public void Record(int companyId, MeteredResource resource, int? entityId) => _inner.Record(companyId, resource, entityId);
            public void EnsureCanOwnAnotherWorkspace(string userId) => _inner.EnsureCanOwnAnotherWorkspace(userId);
            public void EnsureSelfServicePlan(string plan) => _inner.EnsureSelfServicePlan(plan);
            public IDisposable Bypass() => _inner.Bypass();
        }

        private sealed class FixedRemoteIpStartupFilter : IStartupFilter
        {
            private readonly IPAddress _address;

            public FixedRemoteIpStartupFilter(IPAddress address) => _address = address;

            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
            {
                app.Use((context, pipeline) =>
                {
                    context.Connection.RemoteIpAddress = _address;
                    return pipeline(context);
                });
                next(app);
            };
        }
    }
}
