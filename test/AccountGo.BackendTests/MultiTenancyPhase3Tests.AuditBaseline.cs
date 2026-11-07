#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests;

public partial class MultiTenancyPhase3Tests
{
    [Fact]
    public async Task AuditBaseline_SigninReportJournalRoleAndExport()
    {
        int targetMemberId = 0, viewerRoleId = 0;
        WithDb(db =>
        {
            var member = new CompanyMember { CompanyId = _companyId, UserId = _otherId, Role = CompanyMemberRole.Member, Status = CompanyMemberStatus.Active };
            db.CompanyMembers.Add(member);
            db.SaveChanges();
            targetMemberId = member.Id;
            viewerRoleId = db.SecurityRoles.Single(r => r.Name == "Auditor / Viewer").Id;
        });
        long startRecord = 0;
        WithDb(db => startRecord = db.RecordChanges.IgnoreQueryFilters().Select(x => (long?)x.Id).Max() ?? 0);
        int startEvent;
        using (var scope = _factory.Services.CreateScope())
            startEvent = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>().SecurityAuditEvents.Select(x => (int?)x.Id).Max() ?? 0;

        using (var response = await Send(HttpMethod.Post, "/api/auth/send-otp", new { email = "phase3-owner@example.com" }, token: "", scoped: false))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var otp = _emails.Messages.Last(m => m.Subject.StartsWith("AccountGo Sign-In Code: ", StringComparison.Ordinal)).Subject["AccountGo Sign-In Code: ".Length..].Trim();
        using (var response = await Send(HttpMethod.Post, "/api/auth/verify-otp", new { email = "phase3-owner@example.com", otp }, token: "", scoped: false))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await Send(HttpMethod.Get, "/api/Financials/trialbalance?from=2026-01-01&to=2026-12-31"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var journal = LedgerDraft();
        journal.JournalDate = DateTime.ParseExact("2026-02-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        using (var response = await Send(HttpMethod.Post, "/api/Financials/SaveJournalEntry", journal))
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            journal.Id = (await Json(response)).GetProperty("id").GetInt32();
        }
        var approval = await ApproveJournalPostAsync(journal.Id);
        using (var response = await Send(HttpMethod.Post, "/api/Financials/PostJournalEntry", journal, extraHeaders: approval))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await Send(HttpMethod.Patch, $"/api/organizations/current/members/{targetMemberId}", new { securityRoleIds = new[] { viewerRoleId } }))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await Send(HttpMethod.Get, "/api/Financials/ExportSnapshot"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await Task.Delay(800);
        using var readScope = _factory.Services.CreateScope();
        var identity = readScope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
        var security = identity.SecurityAuditEvents.AsNoTracking().Where(x => x.Id > startEvent).AsEnumerable()
            .Select(x => new { type = x.EventType, outcome = x.Outcome, company = x.CompanyId == _companyId ? "workspace" : x.CompanyId == null ? null : "other", user = x.UserId == _ownerId ? "owner" : x.UserId == _otherId ? "member" : x.UserId == null ? null : "other" })
            .OrderBy(x => x.type).ThenBy(x => x.user).ToList();
        var tenant = readScope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var tenantScope = tenant.Impersonate(_companyId, _slug, CompanyMemberRole.Owner);
        var db = readScope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var records = db.RecordChanges.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id > startRecord && x.CompanyId == _companyId).AsEnumerable()
            .Select(x => new { table = x.EntityType, operation = x.Action, keys = JsonDocument.Parse(x.ChangesJson).RootElement.EnumerateObject().Select(p => p.Name).OrderBy(x => x).ToArray() })
            .OrderBy(x => x.table).ThenBy(x => x.operation).ThenBy(x => string.Join(",", x.keys)).ToList();
        var actual = JsonSerializer.SerializeToNode(new { security, records });
        var snapshot = Path.Combine(Path.GetDirectoryName(AuditFixtureSource())!, "Observability", "performance-audit-golden.json");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(snapshot)), actual), "Audit output changed: " + actual.ToJsonString());
    }
    private static string AuditFixtureSource([CallerFilePath] string path = "") => path;
}
