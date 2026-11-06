// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Api.Controllers;
using Core.Domain.Auditing;
using Core.Entitlements;
using Xunit;
using Host = AccountGo.BackendTests.Auditing.AuditTestHost;

namespace AccountGo.BackendTests.Auditing
{
    /// <summary>PR4: includeChildren History and Full-tier CSV export.</summary>
    public sealed class AuditActivityExportTests : IClassFixture<Host>
    {
        private const string ActivityPath = "/api/organizations/current/activity";
        private const string ExportPath = "/api/organizations/current/activity/export";
        private const int ExportCap = 10_000;

        private readonly Host _host;

        public AuditActivityExportTests(Host host) => _host = host;

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

        private void UseEntitlements(int companyId, params string[] features) =>
            _host.Entitlements.UsePlan(companyId, Host.HostedPlan, features);

        private static async Task AssertFeatureNotEntitled(HttpResponseMessage response, string requiredFeature)
        {
            var body = await Host.Json(response);
            Assert.Equal("FEATURE_NOT_ENTITLED", body.GetProperty("code").GetString());
            Assert.Equal(requiredFeature, body.GetProperty("requiredFeature").GetString());
        }

        [Fact]
        public async Task Activity_IncludeChildren_ReturnsHeaderAndLineChanges()
        {
            var workspace = await CreateWorkspace("hist-child");
            await _host.InsertRecordChanges(workspace.CompanyId, 1, entityType: "SalesInvoiceHeader", entityId: 7,
                changesJson: "{\"Memo\":[\"a\",\"b\"]}");
            await _host.InsertRecordChanges(workspace.CompanyId, 1, entityType: "SalesInvoiceLine", entityId: 100,
                parentEntityType: "SalesInvoiceHeader", parentEntityId: 7,
                changesJson: "{\"Amount\":[\"10\",\"20\"]}");
            await _host.InsertRecordChanges(workspace.CompanyId, 1, entityType: "SalesInvoiceLine", entityId: 101,
                parentEntityType: "SalesInvoiceHeader", parentEntityId: 99,
                changesJson: "{\"Amount\":[\"1\",\"2\"]}");

            var withoutChildren = await Host.Json(await _host.Send(HttpMethod.Get,
                ActivityPath + "?entityType=SalesInvoiceHeader&entityId=7",
                token: workspace.Viewer.Token, org: workspace.Slug));
            Assert.Single(withoutChildren.GetProperty("items").EnumerateArray());
            Assert.Equal("SalesInvoiceHeader", withoutChildren.GetProperty("items")[0].GetProperty("entityType").GetString());

            var withChildren = await Host.Json(await _host.Send(HttpMethod.Get,
                ActivityPath + "?entityType=SalesInvoiceHeader&entityId=7&includeChildren=true",
                token: workspace.Viewer.Token, org: workspace.Slug));
            var items = withChildren.GetProperty("items").EnumerateArray().ToList();
            Assert.Equal(2, items.Count);
            Assert.Contains(items, i => i.GetProperty("entityType").GetString() == "SalesInvoiceHeader");
            Assert.Contains(items, i => i.GetProperty("entityType").GetString() == "SalesInvoiceLine"
                && i.GetProperty("entityId").GetInt32() == 100);
            Assert.DoesNotContain(items, i => i.GetProperty("entityId").GetInt32() == 101);
            Assert.Equal(items.Select(i => i.GetProperty("id").GetInt64()).OrderByDescending(id => id),
                items.Select(i => i.GetProperty("id").GetInt64()));
        }

        [Fact]
        public async Task Activity_IncludeChildren_PageOfFifty_StaysWithinQueryBudget()
        {
            var workspace = await CreateWorkspace("hist-budget");
            var actors = new List<Host.UserSession>();
            for (var index = 0; index < 3; index++)
                actors.Add(await _host.CreateUser(Unique("hist-actor") + "@example.com"));
            for (var index = 0; index < 50; index++)
            {
                var actor = actors[index % actors.Count];
                if (index % 2 == 0)
                    await _host.InsertRecordChanges(workspace.CompanyId, 1, entityType: "SalesInvoiceHeader", entityId: 55,
                        actorUserId: actor.Id);
                else
                    await _host.InsertRecordChanges(workspace.CompanyId, 1, entityType: "SalesInvoiceLine", entityId: 1000 + index,
                        parentEntityType: "SalesInvoiceHeader", parentEntityId: 55, actorUserId: actor.Id);
            }

            async Task<int> QueryCount(string path)
            {
                var response = await _host.Send(HttpMethod.Get, path, token: workspace.Viewer.Token, org: workspace.Slug);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                return Host.ServerTimingQueryCount(response);
            }

            var path = ActivityPath + "?entityType=SalesInvoiceHeader&entityId=55&includeChildren=true&limit=50";
            var emptyPath = ActivityPath + "?entityType=SalesInvoiceHeader&entityId=999999&includeChildren=true&limit=50";
            await QueryCount(emptyPath);
            var emptyQueries = await QueryCount(emptyPath);
            var fullQueries = await QueryCount(path);
            // Same envelope as Activity_PageOfFifty: endpoint-owned work (page + batched actors) stays ≤ 12
            // once the fixed tenant/auth/entitlement overhead on an empty page is subtracted.
            const int actorResolutionQueries = 2;
            const int endpointFixedQueries = 7;
            Assert.InRange(fullQueries - emptyQueries, 0, actorResolutionQueries);
            Assert.True(
                fullQueries - emptyQueries + endpointFixedQueries <= 12,
                $"History with children endpoint-owned queries exceeded 12 (empty {emptyQueries}, full {fullQueries}).");
        }

        [Fact]
        public async Task Export_BelowFull_IsForbidden()
        {
            var none = await CreateWorkspace("exp-none");
            var recent = await CreateWorkspace("exp-recent");
            UseEntitlements(none.CompanyId);
            UseEntitlements(recent.CompanyId, FeatureKeys.AuditActivity);

            foreach (var workspace in new[] { none, recent })
            {
                var response = await _host.Send(HttpMethod.Get, ExportPath, token: workspace.Viewer.Token, org: workspace.Slug);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                await AssertFeatureNotEntitled(response, FeatureKeys.AuditHistoryFull);
            }
        }

        [Fact]
        public async Task Export_Full_IsCappedNeutralisedTenantIsolated_AndWritesDataExportEvent()
        {
            var workspaceA = await CreateWorkspace("exp-a");
            var workspaceB = await CreateWorkspace("exp-b");
            UseEntitlements(workspaceA.CompanyId, FeatureKeys.AuditHistoryFull);
            UseEntitlements(workspaceB.CompanyId, FeatureKeys.AuditHistoryFull);

            await _host.InsertRecordChanges(workspaceA.CompanyId, 3, entityType: "ExportA", entityId: 1,
                actorUserId: workspaceA.Viewer.Id,
                changesJson: "{\"Memo\":[\"=1+1\",\"@cmd\"],\"Note\":[\"safe\",\"+profit\"]}");
            await _host.InsertRecordChanges(workspaceB.CompanyId, 2, entityType: "ExportB", entityId: 1,
                actorUserId: workspaceB.Viewer.Id);

            // Cap: seed more than the export limit for tenant A.
            await _host.InsertRecordChanges(workspaceA.CompanyId, ExportCap + 25, entityType: "ExportCap", entityId: 9,
                actorUserId: workspaceA.Viewer.Id,
                firstOccurredAt: DateTime.UtcNow.AddDays(-2),
                spacing: TimeSpan.FromSeconds(1));

            var beforeExport = DateTime.UtcNow.AddSeconds(-1);
            var response = await _host.Send(HttpMethod.Get, ExportPath + "?entityType=ExportCap",
                token: workspaceA.Viewer.Token, org: workspaceA.Slug);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("text/csv", response.Content.Headers.ContentType?.MediaType ?? "", StringComparison.OrdinalIgnoreCase);
            var csv = await response.Content.ReadAsStringAsync();
            // Strip UTF-8 BOM if present.
            if (csv.Length > 0 && csv[0] == '\uFEFF') csv = csv[1..];
            var dataLines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
            Assert.Equal(ExportCap, dataLines.Count);
            Assert.DoesNotContain("ExportB", csv, StringComparison.Ordinal);
            Assert.All(dataLines, line => Assert.Contains("ExportCap", line, StringComparison.Ordinal));

            var formulaResponse = await _host.Send(HttpMethod.Get, ExportPath + "?entityType=ExportA",
                token: workspaceA.Viewer.Token, org: workspaceA.Slug);
            var formulaCsv = await formulaResponse.Content.ReadAsStringAsync();
            if (formulaCsv.Length > 0 && formulaCsv[0] == '\uFEFF') formulaCsv = formulaCsv[1..];
            Assert.Contains("'=1+1", formulaCsv, StringComparison.Ordinal);
            Assert.Contains("'@cmd", formulaCsv, StringComparison.Ordinal);
            Assert.Contains("'+profit", formulaCsv, StringComparison.Ordinal);

            var events = await _host.WaitForEvents(e =>
                e.CompanyId == workspaceA.CompanyId
                && e.EventType == "data.export"
                && e.CreatedAt >= beforeExport);
            Assert.Contains(events, e => e.MetadataJson != null && e.MetadataJson.Contains("activity.export", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("=1+1", "'=1+1")]
        [InlineData("+1", "'+1")]
        [InlineData("-1", "'-1")]
        [InlineData("@cmd", "'@cmd")]
        [InlineData("\t=1+1", "'\t=1+1")]
        [InlineData("\r=1+1", "'\r=1+1")]
        [InlineData("safe", "safe")]
        [InlineData("", "")]
        public void NeutraliseCsvFormula_PrefixesDangerousCells(string input, string expected)
        {
            Assert.Equal(expected, AuditActivityController.NeutraliseCsvFormula(input));
        }
    }
}
