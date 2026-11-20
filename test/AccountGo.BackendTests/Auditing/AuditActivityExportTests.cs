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
using Api.Data;
using Core.Domain.Auditing;
using Core.Entitlements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
            var (inv1Id, inv1Pub, line1Id, line1Pub, inv2Id, line2Id, line2Pub) = await SeedTwoInvoicesWithLines(workspace.CompanyId);
            await _host.InsertRecordChanges(workspace.CompanyId, 1, entityType: "SalesInvoiceHeader", entityId: inv1Id,
                changesJson: "{\"Memo\":[\"a\",\"b\"]}");
            await _host.InsertRecordChanges(workspace.CompanyId, 1, entityType: "SalesInvoiceLine", entityId: line1Id,
                parentEntityType: "SalesInvoiceHeader", parentEntityId: inv1Id,
                changesJson: "{\"Amount\":[\"10\",\"20\"]}");
            await _host.InsertRecordChanges(workspace.CompanyId, 1, entityType: "SalesInvoiceLine", entityId: line2Id,
                parentEntityType: "SalesInvoiceHeader", parentEntityId: inv2Id,
                changesJson: "{\"Amount\":[\"1\",\"2\"]}");

            var withoutChildren = await Host.Json(await _host.Send(HttpMethod.Get,
                ActivityPath + $"?entityType=SalesInvoiceHeader&entityId={inv1Pub}",
                token: workspace.Viewer.Token, org: workspace.Slug));
            var withoutItems = withoutChildren.GetProperty("items").EnumerateArray().ToList();
            Assert.NotEmpty(withoutItems);
            Assert.All(withoutItems, i => Assert.Equal("SalesInvoiceHeader", i.GetProperty("entityType").GetString()));

            var withChildren = await Host.Json(await _host.Send(HttpMethod.Get,
                ActivityPath + $"?entityType=SalesInvoiceHeader&entityId={inv1Pub}&includeChildren=true",
                token: workspace.Viewer.Token, org: workspace.Slug));
            var items = withChildren.GetProperty("items").EnumerateArray().ToList();
            Assert.True(items.Count >= 2);
            Assert.Contains(items, i => i.GetProperty("entityType").GetString() == "SalesInvoiceHeader");
            Assert.Contains(items, i => i.GetProperty("entityType").GetString() == "SalesInvoiceLine"
                && i.GetProperty("entityId").GetString() == line1Pub);
            Assert.DoesNotContain(items, i => i.GetProperty("entityId").GetString() == line2Pub);
            Assert.Equal(items.Select(i => i.GetProperty("id").GetInt64()).OrderByDescending(id => id),
                items.Select(i => i.GetProperty("id").GetInt64()));
        }

        [Fact]
        public async Task Activity_ForeignKeyValuesInChanges_AreShownAsPublicIds()
        {
            var workspace = await CreateWorkspace("hist-fk");
            var (inv1Id, inv1Pub, _, _, _, _, _) = await SeedTwoInvoicesWithLines(workspace.CompanyId);
            int customerId;
            using (var scope = _host.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Api.Data.ApiDbContext>();
                db.SuppressTenantSecurityCheck = true;
                customerId = db.SalesInvoiceHeaders.IgnoreQueryFilters().Single(i => i.Id == inv1Id).CustomerId;
            }
            var customerPub = AccountGo.BackendTests.PublicIds.PublicIdTestHelper.PublicIdOf<Core.Domain.Sales.Customer>(_host.Factory.Services, customerId);
            await _host.InsertRecordChanges(workspace.CompanyId, 1, entityType: "SalesInvoiceHeader", entityId: inv1Id,
                changesJson: "{\"CustomerId\":[null," + customerId + "],\"Memo\":[\"a\",\"b\"]}");

            var body = await Host.Json(await _host.Send(HttpMethod.Get, ActivityPath + $"?entityType=SalesInvoiceHeader&entityId={inv1Pub}",
                token: workspace.Viewer.Token, org: workspace.Slug));
            var changes = body.GetProperty("items").EnumerateArray()
                .Select(i => i.GetProperty("changes"))
                .First(c => c.ValueKind == System.Text.Json.JsonValueKind.Object && c.TryGetProperty("CustomerId", out _));
            Assert.Equal(customerPub, changes.GetProperty("CustomerId")[1].GetString());
            Assert.DoesNotContain(customerId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]", changes.GetRawText());
            Assert.Equal("b", changes.GetProperty("Memo")[1].GetString());
        }

        [Fact]
        public async Task Activity_IncludeChildren_PageOfFifty_StaysWithinQueryBudget()
        {
            var workspace = await CreateWorkspace("hist-budget");
            var (inv1Id, inv1Pub, line1Id, _, inv2Id, _, _) = await SeedTwoInvoicesWithLines(workspace.CompanyId);
            var inv2Pub = AccountGo.BackendTests.PublicIds.PublicIdTestHelper.PublicIdOf<Core.Domain.Sales.SalesInvoiceHeader>(_host.Factory.Services, inv2Id);
            var actors = new List<Host.UserSession>();
            for (var index = 0; index < 3; index++)
                actors.Add(await _host.CreateUser(Unique("hist-actor") + "@example.com"));
            for (var index = 0; index < 50; index++)
            {
                var actor = actors[index % actors.Count];
                if (index % 2 == 0)
                    await _host.InsertRecordChanges(workspace.CompanyId, 1, entityType: "SalesInvoiceHeader", entityId: inv1Id,
                        actorUserId: actor.Id);
                else
                    await _host.InsertRecordChanges(workspace.CompanyId, 1, entityType: "SalesInvoiceLine", entityId: line1Id,
                        parentEntityType: "SalesInvoiceHeader", parentEntityId: inv1Id, actorUserId: actor.Id);
            }

            async Task<int> QueryCount(string path)
            {
                var response = await _host.Send(HttpMethod.Get, path, token: workspace.Viewer.Token, org: workspace.Slug);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                return Host.ServerTimingQueryCount(response);
            }

            var path = ActivityPath + $"?entityType=SalesInvoiceHeader&entityId={inv1Pub}&includeChildren=true&limit=50";
            var emptyPath = ActivityPath + $"?entityType=SalesInvoiceHeader&entityId={inv2Pub}&includeChildren=true&limit=50&from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"))}";
            await QueryCount(emptyPath);
            var emptyQueries = await QueryCount(emptyPath);
            var fullQueries = await QueryCount(path);
            // Endpoint-owned work (page + batched actors + batched PublicId prefetch per prefix) stays bounded (0 N+1).
            const int actorAndPrefetchQueries = 4;
            const int endpointFixedQueries = 7;
            Assert.InRange(fullQueries - emptyQueries, 0, actorAndPrefetchQueries);
            Assert.True(
                fullQueries - emptyQueries + endpointFixedQueries <= 14,
                $"History with children endpoint-owned queries exceeded 14 (empty {emptyQueries}, full {fullQueries}).");
        }

        private async Task<(int Inv1Id, string Inv1Pub, int Line1Id, string Line1Pub, int Inv2Id, int Line2Id, string Line2Pub)> SeedTwoInvoicesWithLines(int companyId)
        {
            using var scope = _host.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.SuppressTenantSecurityCheck = true;
            var customer = new Core.Domain.Sales.Customer { CompanyId = companyId, Party = new Core.Domain.Party { CompanyId = companyId, Name = "Hist Customer", PartyType = Core.Domain.PartyTypes.Customer } };
            db.Customers.Add(customer);
            var uom = db.Measurements.IgnoreQueryFilters().FirstOrDefault(m => m.CompanyId == companyId);
            if (uom == null)
            {
                uom = new Core.Domain.Items.Measurement { CompanyId = companyId, Code = "EA", Description = "Each" };
                db.Measurements.Add(uom);
            }
            var item = db.Items.IgnoreQueryFilters().FirstOrDefault(i => i.CompanyId == companyId);
            if (item == null)
            {
                item = new Core.Domain.Items.Item { CompanyId = companyId, Code = "SVC", Description = "Service" };
                db.Items.Add(item);
            }
            await db.SaveChangesAsync();

            var inv1 = new Core.Domain.Sales.SalesInvoiceHeader { CompanyId = companyId, CustomerId = customer.Id, Date = DateTime.UtcNow.Date, Status = Core.Domain.SalesInvoiceStatus.Draft };
            var l1 = new Core.Domain.Sales.SalesInvoiceLine { CompanyId = companyId, ItemId = item.Id, MeasurementId = uom.Id, Quantity = 1, Amount = 10m };
            inv1.SalesInvoiceLines.Add(l1);
            var inv2 = new Core.Domain.Sales.SalesInvoiceHeader { CompanyId = companyId, CustomerId = customer.Id, Date = DateTime.UtcNow.Date, Status = Core.Domain.SalesInvoiceStatus.Draft };
            var l2 = new Core.Domain.Sales.SalesInvoiceLine { CompanyId = companyId, ItemId = item.Id, MeasurementId = uom.Id, Quantity = 1, Amount = 20m };
            inv2.SalesInvoiceLines.Add(l2);
            db.SalesInvoiceHeaders.AddRange(inv1, inv2);
            await db.SaveChangesAsync();

            return (inv1.Id, inv1.PublicId, l1.Id, l1.PublicId, inv2.Id, l2.Id, l2.PublicId);
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
