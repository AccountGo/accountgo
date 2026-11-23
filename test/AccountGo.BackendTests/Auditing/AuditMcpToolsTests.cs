#nullable enable
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Core.Entitlements;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests.Auditing;

/// <summary>Exercises tool reads with the activity API's real entitlement packaging and controller filters.</summary>
public sealed class AuditMcpToolsTests(AuditTestHost host) : IClassFixture<AuditTestHost>
{
    [Theory]
    [InlineData("single-record", "PlanRestricted", 0)]
    [InlineData("recent", "Success", 90)]
    [InlineData("full", "Success", 0)]
    [InlineData("community", "Success", 0)]
    public async Task HistoryFeed_PlanPackagingMatchesPage_WithoutLimitingSingleRecord(string access, string status, int window)
    {
        var slug = "mcp-history-" + Guid.NewGuid().ToString("N")[..8];
        var user = await host.CreateUser(slug + "@example.com");
        var companyId = await host.CreateWorkspace(user.Token, slug, slug);
        await host.SetFunctionalRoles(companyId, user.Id, "Auditor / Viewer");
        // The probe rows sit on the workspace itself, a record type that has a public id to ask for.
        var companyPublicId = AccountGo.BackendTests.PublicIds.PublicIdTestHelper.PublicIdOf<Core.Domain.Company>(host.Factory.Services, companyId);
        await host.InsertRecordChanges(companyId, 1, DateTime.UtcNow.AddDays(-5), entityType: "Company", entityId: companyId, actorUserId: "probe-actor");
        await host.InsertRecordChanges(companyId, 1, DateTime.UtcNow.AddDays(-200), entityType: "Company", entityId: companyId, actorUserId: "probe-actor");
        host.Entitlements.UsePlan(companyId, access == "community" ? PlanKeys.Community : "pro", access switch
        {
            "recent" => new[] { FeatureKeys.AuditActivity },
            "full" => new[] { FeatureKeys.AuditHistoryFull },
            _ => Array.Empty<string>()
        });

        var record = await CallHistory(user.Token, slug, new { entityType = "Company", entityId = companyPublicId });
        Assert.True(record.GetProperty("status").GetString() == "Success", record.GetRawText());
        var recordActorTokens = record.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("actorUserId").GetString())
            .Where(t => !string.IsNullOrEmpty(t) && t.StartsWith("P-", StringComparison.Ordinal))
            .GroupBy(t => t!)
            .ToList();
        Assert.Contains(recordActorTokens, g => g.Count() == 2);
        Assert.DoesNotContain("probe-actor", record.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(JsonValueKind.Null, record.GetProperty("data").GetProperty("windowDays").ValueKind);

        var feed = await CallHistory(user.Token, slug, new { limit = 100 });
        Assert.True(feed.GetProperty("status").GetString() == status, feed.GetRawText());
        if (status != "Success")
        {
            Assert.False(feed.TryGetProperty("data", out var refusedData) && refusedData.ValueKind != JsonValueKind.Null);
            Assert.False(string.IsNullOrWhiteSpace(feed.GetProperty("message").GetString()));
            return;
        }
        var data = feed.GetProperty("data");
        var companyRows = data.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("entityType").GetString() == "Company")
            .ToList();
        Assert.DoesNotContain("probe-actor", data.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var feedGroups = companyRows
            .Select(i => i.GetProperty("actorUserId").GetString())
            .Where(t => !string.IsNullOrEmpty(t) && t.StartsWith("P-", StringComparison.Ordinal))
            .GroupBy(t => t!)
            .Select(g => g.Count())
            .ToList();
        Assert.Contains(window == 90 ? 1 : 2, feedGroups);
        if (window == 90)
            Assert.Equal(90, data.GetProperty("windowDays").GetInt32());
        else
            Assert.Equal(JsonValueKind.Null, data.GetProperty("windowDays").ValueKind);
    }

    [Theory]
    [InlineData("get_tax_report", "{}", "/api/tax/settings")]
    [InlineData("get_tax_report", "{\"from\":\"2026-01-01\",\"to\":\"2026-01-31\"}", "/api/tax/report?from=2026-01-01&to=2026-01-31")]
    [InlineData("get_equity_statement", "{}", "/api/Financials/EquityStatement")]
    public async Task ReviewReports_PlanWithoutModules_MatchesPageRefusal(string tool, string json, string pagePath)
    {
        var slug = "mcp-no-modules-" + Guid.NewGuid().ToString("N")[..8];
        var user = await host.CreateUser(slug + "@example.com");
        var companyId = await host.CreateWorkspace(user.Token, slug, slug);
        await host.SetFunctionalRoles(companyId, user.Id, "Accountant");
        var arguments = JsonSerializer.Deserialize<JsonElement>(json);
        var allowed = await CallTool(user.Token, slug, tool, arguments);
        Assert.Equal("Success", allowed.GetProperty("status").GetString());

        // Community's mandatory modules cannot be opted out; represent a hosted plan without them.
        host.Entitlements.UseNoModules(companyId);
        using var scope = host.Factory.Services.CreateScope();
        var entitlements = scope.ServiceProvider.GetRequiredService<IEntitlementService>();
        Assert.Throws<ModuleNotSubscribedException>(() =>
            entitlements.Ensure(companyId, new EntitlementAction.Module("financials")));
        using var page = await host.Send(HttpMethod.Get, pagePath, token: user.Token, org: slug);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, page.StatusCode);
        var refused = await CallTool(user.Token, slug, tool, arguments);
        Assert.Equal("PlanRestricted", refused.GetProperty("status").GetString());
        Assert.False(refused.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null);
        Assert.NotEmpty(refused.GetProperty("requiredModules").EnumerateArray());
    }

    private Task<JsonElement> CallHistory(string token, string slug, object arguments) =>
        CallTool(token, slug, "get_record_history", arguments);

    private async Task<JsonElement> CallTool(string token, string slug, string name, object arguments)
    {
        using var client = host.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-AccountGo-Org", slug);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/call",
            @params = new { name, arguments } });
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        var payload = body.TrimStart().StartsWith("{", StringComparison.Ordinal) ? body
            : body.Split('\n').Select(l => l.TrimEnd('\r')).Last(l => l.StartsWith("data: ", StringComparison.Ordinal))[6..];
        using var document = JsonDocument.Parse(payload);
        var result = document.RootElement.GetProperty("result");
        Assert.False(result.TryGetProperty("isError", out var error) && error.GetBoolean(), result.GetRawText());
        using var data = JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!);
        return data.RootElement.Clone();
    }

}
