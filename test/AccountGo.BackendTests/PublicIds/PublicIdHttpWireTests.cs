// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AccountGo.BackendTests.Observability;
using Api.Data;
using Api.Service;
using Core.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace AccountGo.BackendTests.PublicIds;

[Collection("WorkspaceSetupHttp")]
public sealed class PublicIdHttpWireTests
{
    private readonly ITestOutputHelper _output;

    public PublicIdHttpWireTests(ITestOutputHelper output) => _output = output;

    internal static void AssertNoNumericIdsOnWire(JsonNode? node, string endpoint, string path = "$")
    {
        if (node == null)
            return;

        if (node is JsonArray arr)
        {
            for (var i = 0; i < arr.Count; i++)
                AssertNoNumericIdsOnWire(arr[i], endpoint, $"{path}[{i}]");
            return;
        }

        if (node is not JsonObject obj)
            return;

        foreach (var (key, val) in obj)
        {
            if (val == null)
                continue;

            // Raw stored EF column-diff snapshots in RecordChange.ChangesJson are historical audit diffs, not API ID fields.
            if (string.Equals(key, "changes", StringComparison.OrdinalIgnoreCase)
                && endpoint.Contains("/activity", StringComparison.OrdinalIgnoreCase))
                continue;

            // A link built on the server must carry the public id too: /sales/12 or ...?id=5 is an integer key on the wire.
            if (val is JsonValue linkValue && linkValue.TryGetValue<string>(out var linkText)
                && System.Text.RegularExpressions.Regex.IsMatch(linkText, @"^/[A-Za-z][A-Za-z0-9_-]*(/[A-Za-z0-9_-]+)*(/\d+(/|\?|$)|[?&]id=\d+)"))
            {
                Assert.Fail($"Integer key in a link on the wire at {endpoint} -> {path}.{key} = {linkText}");
            }

            var isIdKey = string.Equals(key, "id", StringComparison.OrdinalIgnoreCase)
                || key.EndsWith("Id", StringComparison.Ordinal);
            var isIdsKey = key.EndsWith("Ids", StringComparison.Ordinal);

            if (isIdKey && val is JsonValue jv && jv.TryGetValue<decimal>(out _))
            {
                if (!IsExemptWireField(endpoint, key))
                {
                    Assert.Fail($"Numeric ID found on wire at {endpoint} -> {path}.{key} = {val.ToJsonString()}");
                }
            }
            else if (isIdsKey && val is JsonArray idArr)
            {
                for (var i = 0; i < idArr.Count; i++)
                {
                    if (idArr[i] is JsonValue elemVal && elemVal.TryGetValue<decimal>(out _))
                    {
                        if (!IsExemptWireField(endpoint, key))
                        {
                            Assert.Fail($"Numeric ID found in array on wire at {endpoint} -> {path}.{key}[{i}] = {idArr[i]!.ToJsonString()}");
                        }
                    }
                }
            }
            else
            {
                AssertNoNumericIdsOnWire(val, endpoint, $"{path}.{key}");
            }
        }
    }

    private static bool IsExemptWireField(string endpoint, string propName)
    {
        if (PublicIdExemptions.WireFields.ContainsKey($"*:{propName}"))
            return true;

        var pathOnly = endpoint.Split('?')[0];
        foreach (var key in PublicIdExemptions.WireFields.Keys)
        {
            var sep = key.LastIndexOf(':');
            if (sep <= 0) continue;
            var ep = key[..sep];
            var field = key[(sep + 1)..];
            if (string.Equals(field, propName, StringComparison.OrdinalIgnoreCase)
                && (ep == "*" || pathOnly.Equals(ep, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        return false;
    }

    internal static async Task<(WebApplicationFactory<Program> Factory, HttpClient Client, string Token, string Org1Slug, string Org2Slug, string SqlitePath)> CreateHostAsync()
    {
        var sqlitePath = Path.Combine(Path.GetTempPath(), $"accountgo-pubid-wire-{Guid.NewGuid():N}.db");
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DatabaseProvider", "Sqlite");
            builder.UseSetting("ConnectionStrings:SqliteConnection", $"Data Source={sqlitePath}");
            builder.UseSetting("SEED_DEMO_DATA", "true");
            builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        });

        var client = factory.CreateClient();
        string token;
        using (var scope = factory.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync("support@accountgo.dev")
                ?? throw new InvalidOperationException("Demo support user not found.");
            token = (await services.GetRequiredService<IAuthenticationService>().CreateToken(user, true)).AccessToken;

            var db = services.GetRequiredService<ApiDbContext>();
            var member = db.CompanyMembers.IgnoreQueryFilters()
                .Include(m => m.SecurityRoles)
                .First(m => m.CompanyId == 1 && m.UserId == user.Id);
            member.Role = CompanyMemberRole.Owner;
            var assigned = member.SecurityRoles.Select(r => r.SecurityRoleId).ToHashSet();
            foreach (var roleId in db.SecurityRoles.Where(r => r.Name != "SystemAdministrators").Select(r => r.Id).ToList())
                if (assigned.Add(roleId))
                    db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole { CompanyMemberId = member.Id, SecurityRoleId = roleId });
            await db.SaveChangesAsync();
        }

        // Create a second workspace for cross-tenant 404 oracle testing
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/organizations"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new { name = "Second Workspace", slug = "second-ws", currency = "USD" });
            using var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        }

        // The second workspace's owner needs the same permissions as in the first, so a 404 below can only come from
        // the id lookup (a 403 from the permission filter would hide whether the id leaks across workspaces).
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var company = db.Companies.IgnoreQueryFilters().First(c => c.Slug == "second-ws");
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync("support@accountgo.dev");
            var member = db.CompanyMembers.IgnoreQueryFilters()
                .Include(m => m.SecurityRoles)
                .First(m => m.CompanyId == company.Id && m.UserId == user!.Id);
            var assigned = member.SecurityRoles.Select(r => r.SecurityRoleId).ToHashSet();
            foreach (var roleId in db.SecurityRoles.IgnoreQueryFilters().Where(r => r.Name != "SystemAdministrators").Select(r => r.Id).ToList())
                if (assigned.Add(roleId))
                    db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole { CompanyMemberId = member.Id, SecurityRoleId = roleId });
            await db.SaveChangesAsync();
        }

        return (factory, client, token, "ph-financials", "second-ws", sqlitePath);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token, string slug, HttpMethod method, string path, object? body = null)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("X-AccountGo-Org", slug);
        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }
        return await client.SendAsync(req);
    }

    /// <summary>
    /// Calls every parameterless GET endpoint on the demo workspace and walks the response. Endpoints that return anonymous
    /// objects have no DTO attribute for a test to scan, so this is what catches an integer key they let through.
    /// </summary>
    [Fact]
    public async Task HttpWire_EveryParameterlessGet_CarriesNoNumericIds()
    {
        var (factory, client, token, org1, _, sqlitePath) = await CreateHostAsync();
        try
        {
            var provider = factory.Services.GetRequiredService<Microsoft.AspNetCore.Mvc.ApiExplorer.IApiDescriptionGroupCollectionProvider>();
            var paths = provider.ApiDescriptionGroups.Items.SelectMany(g => g.Items)
                .Where(d => string.Equals(d.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase)
                    && d.RelativePath != null && d.RelativePath.StartsWith("api/", StringComparison.OrdinalIgnoreCase)
                    && !d.RelativePath.Contains('{'))
                .Select(d => "/" + d.RelativePath!.Split('?')[0])
                .Where(path => !path.Contains("/export", StringComparison.OrdinalIgnoreCase)
                    && !path.Contains("/auth/", StringComparison.OrdinalIgnoreCase)
                    && !path.Contains("/stream", StringComparison.OrdinalIgnoreCase)
                    && !path.Contains("/pdf", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Assert.True(paths.Count > 50, $"Expected many GET endpoints, found {paths.Count}.");

            var failures = new List<string>();
            foreach (var path in paths)
            {
                using var response = await SendAsync(client, token, org1, HttpMethod.Get, path);
                if (!response.IsSuccessStatusCode) continue;
                var text = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(text) || (text[0] != '{' && text[0] != '[')) continue;
                try { AssertNoNumericIdsOnWire(JsonNode.Parse(text), path); }
                catch (Exception ex) when (ex.GetType().Name.Contains("Assert", StringComparison.Ordinal) || ex is Xunit.Sdk.XunitException)
                {
                    failures.Add(ex.Message.Split('\n')[0]);
                }
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }
        finally
        {
            factory.Dispose();
            try { File.Delete(sqlitePath); } catch { }
        }
    }

    [Fact]
    public async Task HttpWire_AllMainEndpointsEmitPublicIds_InvoiceFlowWorks_AndErrorsMatchSpec()
    {
        var (factory, client, token, org1, org2, sqlitePath) = await CreateHostAsync();
        try
        {
            // 1. Walk list GETs of public controllers on demo-seeded workspace
            var listEndpoints = new[]
            {
                "/api/organizations",
                "/api/organizations/current",
                "/api/organizations/current/members",
                "/api/organizations/current/security-roles",
                "/api/Administration/company",
                "/api/Administration/financialyears",
                "/api/Administration/paymentterms",
                "/api/Dashboard/Summary",
                "/api/Financials/Accounts",
                "/api/Financials/JournalEntries",
                "/api/Financials/GeneralLedger",
                "/api/Financials/trialbalance",
                "/api/Financials/BalanceSheet",
                "/api/Financials/IncomeStatement",
                "/api/Financials/CashFlow",
                "/api/Banking/Accounts",
                "/api/Sales/SalesInvoices",
                "/api/Sales/Customers",
                "/api/Sales/SalesReceipts",
                "/api/Purchasing/Bills",
                "/api/Purchasing/Vendors",
                "/api/Tax/Taxes",
                "/api/Common/PaymentTerms",
                "/api/Common/Items",
                "/api/Common/Measurements",
                "/api/Common/ItemCategories",
                "/api/Catalog/Items",
                "/api/Stock/Items",
                "/api/Stock/Movements",
                "/api/Donors",
                "/api/Donations/DonationInvoices",
                "/api/RecurringSchedules",
                "/api/sales/credits",
                "/api/purchasing/credits",
                "/api/organizations/current/activity",
            };

            string? firstInvoiceId = null;
            string? firstCustomerId = null;
            string? firstItemId = null;
            string? firstBankId = null;
            string? firstTaxId = null;
            string? firstSalesAccountId = null;

            foreach (var ep in listEndpoints)
            {
                using var res = await SendAsync(client, token, org1, HttpMethod.Get, ep);
                var raw = await res.Content.ReadAsStringAsync();
                Assert.True(res.IsSuccessStatusCode, $"{ep} returned {(int)res.StatusCode}: {raw}");
                var node = JsonNode.Parse(raw);
                AssertNoNumericIdsOnWire(node, ep);

                if (ep == "/api/Sales/SalesInvoices" && node is JsonArray invs && invs.Count > 0)
                    firstInvoiceId = invs[0]?["id"]?.GetValue<string>();
                if (ep == "/api/Sales/Customers" && node is JsonArray cuss && cuss.Count > 0)
                    firstCustomerId = cuss[0]?["id"]?.GetValue<string>();
                if (ep == "/api/Catalog/Items" && node is JsonArray items && items.Count > 0)
                    firstItemId = items[0]?["id"]?.GetValue<string>();
                if (ep == "/api/Banking/Accounts" && node?["accounts"] is JsonArray banks && banks.Count > 0)
                    firstBankId = banks[0]?["id"]?.GetValue<string>();
                if (ep == "/api/Tax/Taxes" && node is JsonArray taxes && taxes.Count > 0)
                    firstTaxId = taxes[0]?["id"]?.GetValue<string>();
                if (ep == "/api/Financials/Accounts" && node is JsonArray accts)
                {
                    var rev = accts.FirstOrDefault(a => a?["accountCode"]?.GetValue<string>() == "40100");
                    firstSalesAccountId = rev?["id"]?.GetValue<string>();
                }
            }

            Assert.NotNull(firstCustomerId);
            Assert.StartsWith("cus_", firstCustomerId);
            Assert.NotNull(firstBankId);
            Assert.StartsWith("ba_", firstBankId);

            // Walk detail GETs
            if (firstInvoiceId != null)
            {
                using var res = await SendAsync(client, token, org1, HttpMethod.Get, $"/api/Sales/SalesInvoice?id={firstInvoiceId}");
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
                AssertNoNumericIdsOnWire(JsonNode.Parse(await res.Content.ReadAsStringAsync()), "/api/Sales/SalesInvoice");
            }
            {
                using var res = await SendAsync(client, token, org1, HttpMethod.Get, $"/api/Banking/Accounts/{firstBankId}/Register");
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
                AssertNoNumericIdsOnWire(JsonNode.Parse(await res.Content.ReadAsStringAsync()), "/api/Banking/Accounts/{id}/Register");
            }

            // 2. End-to-end invoice flow: create with public IDs -> edit line by public line ID -> issue -> pay
            var createBody = new
            {
                customerId = firstCustomerId,
                invoiceDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                status = "Draft",
                salesInvoiceLines = new object[]
                {
                    new
                    {
                        itemId = (string?)null,
                        itemDescription = "Consulting Services",
                        quantity = 2,
                        amount = 150m,
                        taxId = firstTaxId,
                        salesAccountId = firstSalesAccountId
                    }
                }
            };
            using var createRes = await SendAsync(client, token, org1, HttpMethod.Post, "/api/Sales/SaveSalesInvoice", createBody);
            var createRaw = await createRes.Content.ReadAsStringAsync();
            Assert.True(createRes.IsSuccessStatusCode, $"Create invoice failed ({(int)createRes.StatusCode}): {createRaw}");
            var createdInv = JsonNode.Parse(createRaw)!;
            AssertNoNumericIdsOnWire(createdInv, "/api/Sales/SaveSalesInvoice");
            var invPubId = createdInv["id"]!.GetValue<string>();
            var linePubId = createdInv["salesInvoiceLines"]![0]!["id"]!.GetValue<string>();
            Assert.StartsWith("inv_", invPubId);
            Assert.StartsWith("il_", linePubId);

            // Edit line by its public line ID (keep Draft)
            var editBody = new
            {
                id = invPubId,
                customerId = firstCustomerId,
                invoiceDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                status = "Draft",
                salesInvoiceLines = new object[]
                {
                    new
                    {
                        id = linePubId,
                        itemId = (string?)null,
                        itemDescription = "Consulting Services Updated",
                        quantity = 3,
                        amount = 150m,
                        taxId = firstTaxId,
                        salesAccountId = firstSalesAccountId
                    }
                }
            };
            using var editRes = await SendAsync(client, token, org1, HttpMethod.Post, "/api/Sales/SaveSalesInvoice", editBody);
            var editRaw = await editRes.Content.ReadAsStringAsync();
            Assert.True(editRes.IsSuccessStatusCode, $"Edit invoice failed ({(int)editRes.StatusCode}): {editRaw}");
            AssertNoNumericIdsOnWire(JsonNode.Parse(editRaw), "/api/Sales/SaveSalesInvoice");

            // Issue the draft invoice using its public ID
            using var issueRes = await SendAsync(client, token, org1, HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={invPubId}");
            var issueRaw = await issueRes.Content.ReadAsStringAsync();
            Assert.True(issueRes.IsSuccessStatusCode, $"IssueSalesInvoice failed ({(int)issueRes.StatusCode}): {issueRaw}");
            AssertNoNumericIdsOnWire(JsonNode.Parse(issueRaw), "/api/Sales/IssueSalesInvoice");

            // Pay the invoice using public IDs
            var payBody = new
            {
                invoiceId = invPubId,
                amount = 100m,
                paymentDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                bankAccountId = firstBankId
            };
            using var payRes = await SendAsync(client, token, org1, HttpMethod.Post, "/api/Sales/RecordPayment", payBody);
            var payRaw = await payRes.Content.ReadAsStringAsync();
            Assert.True(payRes.IsSuccessStatusCode, $"RecordPayment failed ({(int)payRes.StatusCode}): {payRaw}");
            AssertNoNumericIdsOnWire(JsonNode.Parse(payRaw), "/api/Sales/RecordPayment");

            // 3. Numeric ID -> 400 invalid_id
            using (var numRoute = await SendAsync(client, token, org1, HttpMethod.Get, "/api/Banking/Accounts/1"))
            {
                Assert.Equal(HttpStatusCode.BadRequest, numRoute.StatusCode);
                var body = JsonNode.Parse(await numRoute.Content.ReadAsStringAsync())!;
                Assert.Equal("invalid_id", body["code"]?.GetValue<string>());
            }

            // 4. Malformed & wrong-prefix ID -> 400 invalid_id
            using (var mal = await SendAsync(client, token, org1, HttpMethod.Get, "/api/Sales/SalesInvoice?id=not_valid"))
            {
                Assert.Equal(HttpStatusCode.BadRequest, mal.StatusCode);
                Assert.Equal("invalid_id", JsonNode.Parse(await mal.Content.ReadAsStringAsync())!["code"]?.GetValue<string>());
            }
            using (var wrong = await SendAsync(client, token, org1, HttpMethod.Get, $"/api/Sales/SalesInvoice?id={firstCustomerId}"))
            {
                Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
                Assert.Equal("invalid_id", JsonNode.Parse(await wrong.Content.ReadAsStringAsync())!["code"]?.GetValue<string>());
            }

            // 5. Foreign workspace's valid ID vs random unknown ID -> byte-identical 404 unknown_id
            var randomUnknownInv = PublicIdGenerator.New("inv");
            using var foreignRes = await SendAsync(client, token, org2, HttpMethod.Get, $"/api/Sales/SalesInvoice?id={invPubId}");
            using var unknownRes = await SendAsync(client, token, org2, HttpMethod.Get, $"/api/Sales/SalesInvoice?id={randomUnknownInv}");
            Assert.True(foreignRes.StatusCode == HttpStatusCode.NotFound, $"foreign: {(int)foreignRes.StatusCode} {await foreignRes.Content.ReadAsStringAsync()}");
            Assert.True(unknownRes.StatusCode == HttpStatusCode.NotFound, $"unknown: {(int)unknownRes.StatusCode} {await unknownRes.Content.ReadAsStringAsync()}");
            var foreignBytes = await foreignRes.Content.ReadAsByteArrayAsync();
            var unknownBytes = await unknownRes.Content.ReadAsByteArrayAsync();
            Assert.Equal(unknownBytes, foreignBytes);
            Assert.Equal("unknown_id", JsonNode.Parse(Encoding.UTF8.GetString(foreignBytes))!["code"]?.GetValue<string>());
        }
        finally
        {
            client.Dispose();
            await factory.DisposeAsync();
            if (File.Exists(sqlitePath)) File.Delete(sqlitePath);
        }
    }
}
