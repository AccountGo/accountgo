// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Core.Domain.Auditing;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Host = AccountGo.BackendTests.Auditing.AuditTestHost;

namespace AccountGo.BackendTests.Auditing
{
    /// <summary>
    /// PR3 activity APIs: <c>/api/organizations/current/activity</c>, <c>/security-events</c> and
    /// <c>/api/Administration/auditlogs</c> (tenancy, paging, viewer packaging, query budget).
    /// </summary>
    public sealed class AuditActivityApiTests : IClassFixture<Host>
    {
        private const string ActivityPath = "/api/organizations/current/activity";
        private const string SecurityEventsPath = "/api/organizations/current/security-events";
        private const string AuditLogsPath = "/api/Administration/auditlogs";
        private const int DefaultPageSize = 50;
        private const int MaxPageSize = 100;
        private const int PagingRowCount = 120;
        private const int MaxQueriesPerActivityPage = 12;
        private const int ActorResolutionQueries = 2;
        // Page query plus the community entitlement snapshot lookup the controller performs itself.
        private const int ActivityEndpointFixedQueries = 7;
        private const int RecentWindowDays = 90;
        private const int AuditLogRowCount = 250;

        private readonly Host _host;

        public AuditActivityApiTests(Host host) => _host = host;

        private static string Unique(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..8];

        private sealed record Workspace(string Slug, int CompanyId, Host.UserSession Viewer);

        private async Task<Workspace> CreateWorkspace(string prefix)
        {
            var slug = Unique(prefix);
            var viewer = await _host.CreateUser($"viewer-{slug}@example.com");
            var companyId = await _host.CreateWorkspace(viewer.Token, "Audit Org " + slug, slug);
            await _host.SetFunctionalRoles(companyId, viewer.Id, "Auditor / Viewer");
            return new Workspace(slug, companyId, viewer);
        }

        private async Task<JsonElement> GetOk(Workspace workspace, string path)
        {
            var response = await _host.Send(HttpMethod.Get, path, token: workspace.Viewer.Token, org: workspace.Slug);
            Assert.True(response.IsSuccessStatusCode, path + " -> " + response.StatusCode + " " + await response.Content.ReadAsStringAsync());
            return await Host.Json(response);
        }

        private static string Iso(DateTime utc) =>
            Uri.EscapeDataString(DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture));

        private static List<long> ItemIds(JsonElement page) =>
            page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ToList();

        private static List<string> ItemEntityTypes(JsonElement page) =>
            page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("entityType").GetString()!).ToList();

        private void UseEntitlements(int companyId, params string[] features) =>
            _host.Entitlements.UsePlan(companyId, Host.HostedPlan, features);

        private static async Task AssertFeatureNotEntitled(HttpResponseMessage response, string requiredFeature)
        {
            var body = await Host.Json(response);
            Assert.Equal("FEATURE_NOT_ENTITLED", body.GetProperty("code").GetString());
            Assert.Equal(requiredFeature, body.GetProperty("requiredFeature").GetString());
        }

        // ---- activity: tenancy ------------------------------------------------------------------------

        [Fact]
        public async Task Activity_IsTenantIsolated()
        {
            var workspaceA = await CreateWorkspace("act-a");
            var workspaceB = await CreateWorkspace("act-b");
            await _host.InsertRecordChanges(workspaceA.CompanyId, 3, entityType: "TenantA");
            await _host.InsertRecordChanges(workspaceB.CompanyId, 4, entityType: "TenantB");

            var pageA = await GetOk(workspaceA, ActivityPath + "?limit=100");
            var pageB = await GetOk(workspaceB, ActivityPath + "?limit=100");

            Assert.Equal(3, ItemEntityTypes(pageA).Count(t => t == "TenantA"));
            Assert.DoesNotContain("TenantB", ItemEntityTypes(pageA));
            Assert.Equal(4, ItemEntityTypes(pageB).Count(t => t == "TenantB"));
            Assert.DoesNotContain("TenantA", ItemEntityTypes(pageB));

            var crossLookup = await GetOk(workspaceA, ActivityPath + "?entityType=TenantB&entityId=1");
            Assert.Empty(crossLookup.GetProperty("items").EnumerateArray());
        }

        [Fact]
        public async Task Activity_NonMemberOfWorkspace_IsRejected()
        {
            var workspaceA = await CreateWorkspace("act-nm-a");
            var workspaceB = await CreateWorkspace("act-nm-b");
            await _host.InsertRecordChanges(workspaceB.CompanyId, 2, entityType: "TenantB");

            var response = await _host.Send(HttpMethod.Get, ActivityPath, token: workspaceA.Viewer.Token, org: workspaceB.Slug);

            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("TenantB", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Activity_WithoutAuditLogsViewPermission_IsForbidden()
        {
            var slug = Unique("act-noperm");
            var owner = await _host.CreateUser($"zero-{slug}@example.com");
            await _host.CreateWorkspace(owner.Token, "Zero", slug);

            var activity = await _host.Send(HttpMethod.Get, ActivityPath, token: owner.Token, org: slug);
            var security = await _host.Send(HttpMethod.Get, SecurityEventsPath, token: owner.Token, org: slug);

            Assert.Equal(HttpStatusCode.Forbidden, activity.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, security.StatusCode);
        }

        [Fact]
        public async Task Activity_ReturnsChangesAsJson_AndResolvesActorName()
        {
            var workspace = await CreateWorkspace("act-shape");
            await _host.InsertRecordChanges(workspace.CompanyId, 1, entityType: "Shape", entityId: 5, actorUserId: workspace.Viewer.Id);

            var page = await GetOk(workspace, ActivityPath + "?entityType=Shape&entityId=5");

            var item = Assert.Single(page.GetProperty("items").EnumerateArray());
            Assert.Equal("Modified", item.GetProperty("action").GetString());
            Assert.Equal("b", item.GetProperty("changes").GetProperty("Name").GetProperty("new").GetString());
            Assert.Equal(workspace.Viewer.Id, item.GetProperty("actorUserId").GetString());
            Assert.Equal(workspace.Viewer.Email, item.GetProperty("actorName").GetString());
            Assert.Equal("user", item.GetProperty("actorKind").GetString());
            Assert.StartsWith("seed-", item.GetProperty("correlationId").GetString());
        }

        // ---- activity: keyset paging ------------------------------------------------------------------

        [Fact]
        public async Task Activity_KeysetPaging_WalksEveryRowOnce_InDescendingIdOrder()
        {
            var workspace = await CreateWorkspace("act-page");
            await _host.InsertRecordChanges(workspace.CompanyId, PagingRowCount, entityType: "Paging");

            var seen = new List<long>();
            var pageSizes = new List<int>();
            long? cursor = null;
            var guard = 0;
            do
            {
                var path = ActivityPath + "?entityType=Paging&limit=" + DefaultPageSize
                    + (cursor.HasValue ? "&cursor=" + cursor.Value.ToString(CultureInfo.InvariantCulture) : "");
                var page = await GetOk(workspace, path);
                var ids = ItemIds(page);
                pageSizes.Add(ids.Count);
                seen.AddRange(ids);
                var next = page.GetProperty("nextCursor");
                cursor = next.ValueKind == JsonValueKind.Null ? null : next.GetInt64();
                if (cursor.HasValue)
                    Assert.Equal(ids[^1], cursor.Value);
            }
            while (cursor.HasValue && ++guard < 10);

            Assert.Equal(new[] { DefaultPageSize, DefaultPageSize, PagingRowCount - 2 * DefaultPageSize }, pageSizes);
            Assert.Equal(PagingRowCount, seen.Count);
            Assert.Equal(PagingRowCount, seen.Distinct().Count());
            Assert.Equal(seen.OrderByDescending(id => id).ToList(), seen);

            using var scope = _host.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var expected = db.RecordChanges.IgnoreQueryFilters()
                .Where(r => r.CompanyId == workspace.CompanyId && r.EntityType == "Paging")
                .Select(r => r.Id)
                .ToList();
            Assert.Equal(expected.OrderBy(id => id), seen.OrderBy(id => id));
        }

        [Fact]
        public async Task Activity_LimitIsClampedToServerBounds()
        {
            var workspace = await CreateWorkspace("act-limit");
            await _host.InsertRecordChanges(workspace.CompanyId, MaxPageSize + 20, entityType: "Clamp");

            var oversized = await GetOk(workspace, ActivityPath + "?entityType=Clamp&limit=5000");
            var zero = await GetOk(workspace, ActivityPath + "?entityType=Clamp&limit=0");

            Assert.Equal(MaxPageSize, ItemIds(oversized).Count);
            Assert.Equal(DefaultPageSize, ItemIds(zero).Count);
        }

        // ---- activity: viewer packaging ---------------------------------------------------------------

        [Fact]
        public async Task Activity_SingleRecordOnly_ListIsForbidden_ButSingleRecordStillWorks()
        {
            var workspace = await CreateWorkspace("act-single");
            await _host.InsertRecordChanges(workspace.CompanyId, 3, entityType: "Invoice", entityId: 77);
            UseEntitlements(workspace.CompanyId);

            var list = await _host.Send(HttpMethod.Get, ActivityPath, token: workspace.Viewer.Token, org: workspace.Slug);
            Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
            await AssertFeatureNotEntitled(list, FeatureKeys.AuditActivity);

            var filteredByTypeOnly = await _host.Send(HttpMethod.Get, ActivityPath + "?entityType=Invoice", token: workspace.Viewer.Token, org: workspace.Slug);
            Assert.Equal(HttpStatusCode.Forbidden, filteredByTypeOnly.StatusCode);
            await AssertFeatureNotEntitled(filteredByTypeOnly, FeatureKeys.AuditActivity);

            var single = await GetOk(workspace, ActivityPath + "?entityType=Invoice&entityId=77");
            Assert.Equal(3, ItemIds(single).Count);
            Assert.All(single.GetProperty("items").EnumerateArray(), item =>
            {
                Assert.Equal("Invoice", item.GetProperty("entityType").GetString());
                Assert.Equal(77, item.GetProperty("entityId").GetInt32());
            });
            Assert.Equal(JsonValueKind.Null, single.GetProperty("windowDays").ValueKind);
        }

        [Fact]
        public async Task Activity_Recent90Days_ClampsWindow_AndReportsWindowDays()
        {
            var workspace = await CreateWorkspace("act-90");
            var now = DateTime.UtcNow;
            await _host.InsertRecordChanges(workspace.CompanyId, 1, now.AddDays(-10), entityType: "Window", entityId: 1);
            await _host.InsertRecordChanges(workspace.CompanyId, 1, now.AddDays(-60), entityType: "Window", entityId: 2);
            await _host.InsertRecordChanges(workspace.CompanyId, 1, now.AddDays(-RecentWindowDays - 10), entityType: "Window", entityId: 3);
            await _host.InsertRecordChanges(workspace.CompanyId, 1, now.AddDays(-200), entityType: "Window", entityId: 4);
            UseEntitlements(workspace.CompanyId, FeatureKeys.AuditActivity);

            var defaultWindow = await GetOk(workspace, ActivityPath + "?entityType=Window");
            Assert.Equal(RecentWindowDays, defaultWindow.GetProperty("windowDays").GetInt32());
            Assert.Equal(2, ItemIds(defaultWindow).Count);

            // A caller-supplied `from` older than the window is clamped up to the window floor.
            var widened = await GetOk(workspace, ActivityPath + "?entityType=Window&from=" + Iso(now.AddDays(-365)));
            Assert.Equal(RecentWindowDays, widened.GetProperty("windowDays").GetInt32());
            Assert.Equal(2, ItemIds(widened).Count);

            // A `from` inside the window is honored.
            var narrowed = await GetOk(workspace, ActivityPath + "?entityType=Window&from=" + Iso(now.AddDays(-30)));
            Assert.Equal(RecentWindowDays, narrowed.GetProperty("windowDays").GetInt32());
            var narrowedItem = Assert.Single(narrowed.GetProperty("items").EnumerateArray());
            Assert.Equal(1, narrowedItem.GetProperty("entityId").GetInt32());

            // Single-record history is not windowed.
            var oldRecord = await GetOk(workspace, ActivityPath + "?entityType=Window&entityId=4");
            Assert.Single(oldRecord.GetProperty("items").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, oldRecord.GetProperty("windowDays").ValueKind);
        }

        [Fact]
        public async Task Activity_FullViaFeature_ReturnsAllRows_WithoutWindow()
        {
            var workspace = await CreateWorkspace("act-full");
            var now = DateTime.UtcNow;
            await _host.InsertRecordChanges(workspace.CompanyId, 1, now.AddDays(-5), entityType: "Everything", entityId: 1);
            await _host.InsertRecordChanges(workspace.CompanyId, 1, now.AddDays(-400), entityType: "Everything", entityId: 2);
            UseEntitlements(workspace.CompanyId, FeatureKeys.AuditHistoryFull);

            var page = await GetOk(workspace, ActivityPath + "?entityType=Everything");

            Assert.Equal(2, ItemIds(page).Count);
            Assert.Equal(JsonValueKind.Null, page.GetProperty("windowDays").ValueKind);
        }

        [Fact]
        public async Task Activity_CommunityPlan_ReturnsAllRows_WithoutWindow()
        {
            var workspace = await CreateWorkspace("act-comm");
            var now = DateTime.UtcNow;
            await _host.InsertRecordChanges(workspace.CompanyId, 1, now.AddDays(-5), entityType: "Everything", entityId: 1);
            await _host.InsertRecordChanges(workspace.CompanyId, 1, now.AddDays(-400), entityType: "Everything", entityId: 2);

            var page = await GetOk(workspace, ActivityPath + "?entityType=Everything");

            Assert.Equal(2, ItemIds(page).Count);
            Assert.Equal(JsonValueKind.Null, page.GetProperty("windowDays").ValueKind);
        }

        // ---- activity: query budget -------------------------------------------------------------------

        [Fact]
        public async Task Activity_PageOfFifty_AddsNoPerRowQueries_ViaServerTiming()
        {
            var workspace = await CreateWorkspace("act-budget");
            var actors = new List<Host.UserSession>();
            for (var index = 0; index < 3; index++)
                actors.Add(await _host.CreateUser(Unique("actor") + "@example.com"));
            foreach (var actor in actors)
                await _host.InsertRecordChanges(workspace.CompanyId, 40, entityType: "Budget", actorUserId: actor.Id);

            // Warm per-process caches, then compare an empty page (tenant/auth/entitlement overhead plus the page query)
            // with a full page of 50 rows spread across several actors.
            await QueryCount(workspace, ActivityPath + "?entityType=Nothing&limit=1");
            var emptyPageQueries = await QueryCount(workspace, ActivityPath + "?entityType=Nothing&limit=1");
            var fullPageQueries = await QueryCount(workspace, ActivityPath + "?entityType=Budget&limit=" + DefaultPageSize, expectedItems: DefaultPageSize);
            var smallPageQueries = await QueryCount(workspace, ActivityPath + "?entityType=Budget&limit=5", expectedItems: 5);

            // Actor-name resolution is the only extra work and is batched: identity users, then profile names.
            Assert.InRange(fullPageQueries - emptyPageQueries, 0, ActorResolutionQueries);
            Assert.Equal(smallPageQueries, fullPageQueries);
            Assert.True(
                fullPageQueries - emptyPageQueries + ActivityEndpointFixedQueries <= MaxQueriesPerActivityPage,
                $"Activity endpoint-owned queries exceeded {MaxQueriesPerActivityPage} (empty page {emptyPageQueries}, full page {fullPageQueries}).");
        }

        private async Task<int> QueryCount(Workspace workspace, string path, int? expectedItems = null)
        {
            var response = await _host.Send(HttpMethod.Get, path, token: workspace.Viewer.Token, org: workspace.Slug);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            if (expectedItems.HasValue)
            {
                var body = await Host.Json(response);
                Assert.Equal(expectedItems.Value, ItemIds(body).Count);
                Assert.All(body.GetProperty("items").EnumerateArray(), item =>
                    Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("actorName").GetString())));
            }
            return Host.ServerTimingQueryCount(response);
        }

        // ---- security events --------------------------------------------------------------------------

        [Fact]
        public async Task SecurityEvents_AreTenantIsolated_AndExcludePlatformEvents()
        {
            var workspaceA = await CreateWorkspace("sec-a");
            var workspaceB = await CreateWorkspace("sec-b");
            var now = DateTime.UtcNow;
            await _host.InsertSecurityEvents(new[]
            {
                NewEvent("test.tenant.a", workspaceA.CompanyId, now.AddMinutes(-3)),
                NewEvent("test.tenant.a", workspaceA.CompanyId, now.AddMinutes(-2)),
                NewEvent("test.tenant.b", workspaceB.CompanyId, now.AddMinutes(-2)),
                NewEvent("test.platform", null, now.AddMinutes(-1))
            });

            var pageA = await GetOk(workspaceA, SecurityEventsPath + "?limit=100");
            var pageB = await GetOk(workspaceB, SecurityEventsPath + "?limit=100");

            var typesA = pageA.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("type").GetString()).ToList();
            var typesB = pageB.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("type").GetString()).ToList();
            Assert.Equal(2, typesA.Count(t => t == "test.tenant.a"));
            Assert.DoesNotContain("test.tenant.b", typesA);
            Assert.DoesNotContain("test.platform", typesA);
            Assert.Equal(1, typesB.Count(t => t == "test.tenant.b"));
            Assert.DoesNotContain("test.tenant.a", typesB);
            Assert.DoesNotContain("test.platform", typesB);
        }

        [Fact]
        public async Task SecurityEvents_ReturnShapeAndKeysetPaging()
        {
            var workspace = await CreateWorkspace("sec-page");
            var now = DateTime.UtcNow;
            await _host.InsertSecurityEvents(Enumerable.Range(0, 5).Select(index =>
                NewEvent("test.paging", workspace.CompanyId, now.AddMinutes(-index), userId: workspace.Viewer.Id)));

            var first = await GetOk(workspace, SecurityEventsPath + "?type=test.paging&limit=2");
            var second = await GetOk(workspace, SecurityEventsPath + "?type=test.paging&limit=2&cursor=" + first.GetProperty("nextCursor").GetInt32());
            var third = await GetOk(workspace, SecurityEventsPath + "?type=test.paging&limit=2&cursor=" + second.GetProperty("nextCursor").GetInt32());

            var ids = new[] { first, second, third }
                .SelectMany(p => p.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32()))
                .ToList();
            Assert.Equal(5, ids.Count);
            Assert.Equal(5, ids.Distinct().Count());
            Assert.Equal(ids.OrderByDescending(id => id).ToList(), ids);
            Assert.Equal(JsonValueKind.Null, third.GetProperty("nextCursor").ValueKind);

            var item = first.GetProperty("items")[0];
            Assert.Equal("test.paging", item.GetProperty("type").GetString());
            Assert.Equal("success", item.GetProperty("outcome").GetString());
            Assert.Equal("corr-test", item.GetProperty("correlationId").GetString());
            Assert.Equal("198.51.100.9", item.GetProperty("ipAddress").GetString());
            Assert.Equal(workspace.Viewer.Id, item.GetProperty("actorUserId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("actorName").GetString()));
            Assert.Equal("value", item.GetProperty("metadata").GetProperty("key").GetString());
        }

        [Fact]
        public async Task SecurityEvents_BelowFull_AreForbidden_WithStableCode()
        {
            var noFeatures = await CreateWorkspace("sec-none");
            var recent = await CreateWorkspace("sec-recent");
            UseEntitlements(noFeatures.CompanyId);
            UseEntitlements(recent.CompanyId, FeatureKeys.AuditActivity);

            foreach (var workspace in new[] { noFeatures, recent })
            {
                var response = await _host.Send(HttpMethod.Get, SecurityEventsPath, token: workspace.Viewer.Token, org: workspace.Slug);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                await AssertFeatureNotEntitled(response, FeatureKeys.AuditHistoryFull);
            }
        }

        [Fact]
        public async Task SecurityEvents_FullViaFeature_AreReadable()
        {
            var workspace = await CreateWorkspace("sec-full");
            await _host.InsertSecurityEvents(new[] { NewEvent("test.full", workspace.CompanyId, DateTime.UtcNow) });
            UseEntitlements(workspace.CompanyId, FeatureKeys.AuditHistoryFull);

            var page = await GetOk(workspace, SecurityEventsPath + "?type=test.full");

            Assert.Single(page.GetProperty("items").EnumerateArray());
        }

        private static SecurityAuditEvent NewEvent(string type, int? companyId, DateTime createdAt, string? userId = null) => new()
        {
            EventType = type,
            CompanyId = companyId,
            UserId = userId,
            Outcome = "success",
            CorrelationId = "corr-test",
            IpAddress = "198.51.100.9",
            MetadataJson = "{\"key\":\"value\"}",
            CreatedAt = createdAt
        };

        // ---- /api/Administration/auditlogs ------------------------------------------------------------

        [Fact]
        public async Task AuditLogs_ReturnsAtMostOneHundred_AndPagesWithBeforeId()
        {
            var workspace = await CreateWorkspace("auditlogs");
            var other = await CreateWorkspace("auditlogs-other");
            await SeedAuditLogs(workspace.CompanyId, AuditLogRowCount, "AuditLogsTest");
            await SeedAuditLogs(other.CompanyId, 5, "OtherTenantTable");

            var firstPage = await GetOk(workspace, AuditLogsPath);
            var firstIds = firstPage.EnumerateArray().Select(r => r.GetProperty("id").GetInt32()).ToList();
            Assert.Equal(MaxPageSize, firstIds.Count);
            Assert.Equal(firstIds.OrderByDescending(id => id).ToList(), firstIds);

            var oversized = await GetOk(workspace, AuditLogsPath + "?limit=5000");
            Assert.Equal(MaxPageSize, oversized.GetArrayLength());
            var small = await GetOk(workspace, AuditLogsPath + "?limit=10");
            Assert.Equal(10, small.GetArrayLength());

            var all = new List<int>(firstIds);
            var guard = 0;
            var lastId = firstIds[^1];
            while (guard++ < 20)
            {
                var page = await GetOk(workspace, AuditLogsPath + "?beforeId=" + lastId);
                var ids = page.EnumerateArray().Select(r => r.GetProperty("id").GetInt32()).ToList();
                Assert.True(ids.Count <= MaxPageSize);
                if (ids.Count == 0)
                    break;
                Assert.All(ids, id => Assert.True(id < lastId));
                all.AddRange(ids);
                lastId = ids[^1];
            }

            Assert.Equal(all.Count, all.Distinct().Count());
            using var scope = _host.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var expected = db.AuditLogs.IgnoreQueryFilters()
                .Where(a => a.CompanyId == workspace.CompanyId
                    && a.TableName != Core.Authorization.ApprovalEvidence.ApprovalTable
                    && a.TableName != Core.Authorization.ApprovalEvidence.StateTable)
                .Select(a => a.Id)
                .ToList();
            Assert.True(expected.Count >= AuditLogRowCount);
            Assert.Equal(expected.OrderBy(id => id), all.OrderBy(id => id));
        }

        private async Task SeedAuditLogs(int companyId, int count, string tableName)
        {
            using var scope = _host.Factory.Services.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var impersonation = tenant.Impersonate(companyId, role: CompanyMemberRole.Owner);
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            for (var index = 0; index < count; index++)
            {
                db.AuditLogs.Add(new AuditLog
                {
                    CompanyId = companyId,
                    UserName = "seed",
                    AuditEventDateUTC = DateTime.UtcNow.AddSeconds(-index),
                    AuditEventType = (int)AuditEventTypes.Modified,
                    TableName = tableName,
                    RecordId = (index + 1).ToString(CultureInfo.InvariantCulture),
                    FieldName = "Name",
                    OriginalValue = "a",
                    NewValue = "b"
                });
            }
            await db.SaveChangesAsync();
        }
    }
}
