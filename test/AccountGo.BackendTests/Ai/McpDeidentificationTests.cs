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
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Api.Service;
using Api.Service.Ai;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Xunit;

namespace AccountGo.BackendTests.Ai;

public sealed class McpDeidentificationTests : IAsyncLifetime
{
    private const string TestAnterApiKey = "proj_api-key_deid_test_gate_secret";
    private const string SentinelCustomerName = "Zeloria Holdings Sentinel";
    private const string SentinelVendorName = "Kestrel Supply Sentinel";
    private const string SentinelBankName = "Veridian Checking Sentinel";
    private const string SentinelEmail = "sentinel.pii@example.test";
    private const string SentinelPhone = "+1-555-867-5309";
    private const string SentinelAddress = "742 Evergreen Terrace Sentinel";
    private const string SentinelIban = "DE89370400440532013000";
    private const string SentinelFreeText = "SECRET FREE TEXT MEMO SENTINEL";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), "accountgo_mcp_deid_" + Guid.NewGuid().ToString("N") + ".db");
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private string _token = null!;
    private string _userId = null!;
    private string _slug = null!;
    private int _companyId;
    private string _customerPublicId = null!;
    private string _vendorPublicId = null!;
    private string _bankPublicId = null!;
    private string _invoicePublicId = null!;
    private string _billPublicId = null!;
    private string _itemPublicId = null!;
    private int _rpcId = 1;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DatabaseProvider", "Sqlite");
            builder.UseSetting("ConnectionStrings:SqliteConnection", "Data Source=" + _dbPath);
            builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
            builder.UseSetting("Anter:ApiKey", TestAnterApiKey);
            builder.UseSetting("ANTER_API_KEY", TestAnterApiKey);
            builder.UseSetting("PUBLIC_WEB_URL", "https://workspace.example");
            builder.UseSetting("PUBLIC_API_URL", "https://api.example");
        });

        _client = _factory.CreateClient();

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = "deid-tester@example.com",
                Email = "deid-tester@example.com",
                EmailConfirmed = true,
            };
            var created = await userManager.CreateAsync(user);
            Assert.True(created.Succeeded);
            _userId = user.Id;

            var authService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            var dto = await authService.CreateToken(user, populateExp: true);
            _token = dto.AccessToken;
        }

        _slug = "deid-org-" + Guid.NewGuid().ToString("N")[..8];
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/organizations"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            req.Content = JsonContent.Create(new { name = "Deid Test Org", slug = _slug });
            var res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            _companyId = AccountGo.BackendTests.PublicIds.PublicIdTestHelper.ResolveCompanyId(
                _factory.Services, doc.RootElement.GetProperty("id").GetString()!);
        }

        await SeedSentinelDataAsync();
    }

    private async Task SeedSentinelDataAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>();
        using var _ = tenant.Impersonate(_companyId, _slug);
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();

        var ownerMember = await db.CompanyMembers.SingleAsync(m => m.CompanyId == _companyId && m.UserId == _userId);
        var accountantRole = await db.SecurityRoles.SingleAsync(r => r.Name == "Accountant");
        db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole
        {
            CompanyMemberId = ownerMember.Id,
            SecurityRoleId = accountantRole.Id,
        });

        var customerParty = new Party
        {
            CompanyId = _companyId,
            Name = SentinelCustomerName,
            Email = SentinelEmail,
            Phone = SentinelPhone,
            CompanyStreet = SentinelAddress,
            PartyType = PartyTypes.Customer,
            IsActive = true,
        };
        var customer = new Customer
        {
            CompanyId = _companyId,
            No = "CUST-9001",
            Party = customerParty,
        };
        db.Customers.Add(customer);

        var vendorParty = new Party
        {
            CompanyId = _companyId,
            Name = SentinelVendorName,
            Email = SentinelEmail,
            Phone = SentinelPhone,
            CompanyStreet = SentinelAddress,
            PartyType = PartyTypes.Vendor,
            IsActive = true,
        };
        var vendor = new Vendor
        {
            CompanyId = _companyId,
            No = "VEND-9001",
            Party = vendorParty,
        };
        db.Vendors.Add(vendor);

        var bankLedgerAccount = await db.Accounts.IgnoreQueryFilters()
            .FirstAsync(a => a.CompanyId == _companyId && a.IsCash);
        bankLedgerAccount.AccountName = SentinelBankName;
        bankLedgerAccount.Description = SentinelFreeText;

        var bank = new Bank
        {
            CompanyId = _companyId,
            Name = SentinelBankName,
            BankName = SentinelBankName,
            Number = SentinelIban,
            AccountId = bankLedgerAccount.Id,
            IsActive = true,
        };
        db.Banks.Add(bank);

        await db.SaveChangesAsync();

        var revenueAccount = await db.Accounts.IgnoreQueryFilters()
            .FirstAsync(a => a.CompanyId == _companyId && a.AccountClassId == 4);
        var expenseAccount = await db.Accounts.IgnoreQueryFilters()
            .FirstAsync(a => a.CompanyId == _companyId && a.AccountClassId == 5);
        var item = await db.Items.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.CompanyId == _companyId);
        if (item is null)
        {
            var measurement = await db.Measurements.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.CompanyId == _companyId);
            if (measurement is null)
            {
                measurement = new Core.Domain.Items.Measurement { CompanyId = _companyId, Code = "EA", Description = "Each" };
                db.Measurements.Add(measurement);
                await db.SaveChangesAsync();
            }

            item = new Core.Domain.Items.Item
            {
                CompanyId = _companyId,
                Code = "SVC-001",
                Description = "Consulting Service",
                SalesAccountId = revenueAccount.Id,
                CostOfGoodsSoldAccountId = expenseAccount.Id,
                SmallestMeasurementId = measurement.Id,
                SellMeasurementId = measurement.Id,
                PurchaseMeasurementId = measurement.Id,
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();
        }

        var invoice = new SalesInvoiceHeader
        {
            CompanyId = _companyId,
            No = "INV-9001",
            CustomerId = customer.Id,
            Date = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc),
            DueDate = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc),
            Status = SalesInvoiceStatus.Open,
            ReferenceNo = "REF-9001",
            Notes = SentinelFreeText,
            SalesInvoiceLines = new List<SalesInvoiceLine>
            {
                new()
                {
                    CompanyId = _companyId,
                    ItemId = item.Id,
                    MeasurementId = item.SellMeasurementId ?? 1,
                    Description = SentinelFreeText,
                    Quantity = 2m,
                    Amount = 125.50m,
                }
            }
        };
        db.SalesInvoiceHeaders.Add(invoice);

        var bill = new PurchaseInvoiceHeader
        {
            CompanyId = _companyId,
            No = "BILL-9001",
            VendorId = vendor.Id,
            Date = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc),
            DueDate = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc),
            Status = PurchaseInvoiceStatus.Open,
            VendorInvoiceNo = "VINV-9001",
            ReferenceNo = "BREF-9001",
            Description = SentinelFreeText,
            PurchaseInvoiceLines = new List<PurchaseInvoiceLine>
            {
                new()
                {
                    CompanyId = _companyId,
                    ItemId = item.Id,
                    AccountId = expenseAccount.Id,
                    Description = SentinelFreeText,
                    Quantity = 1m,
                    Amount = 88.25m,
                }
            }
        };
        db.PurchaseInvoiceHeaders.Add(bill);

        await db.SaveChangesAsync();

        _customerPublicId = customer.PublicId;
        _vendorPublicId = vendor.PublicId;
        _bankPublicId = bank.PublicId;
        _invoicePublicId = invoice.PublicId;
        _billPublicId = bill.PublicId;
        _itemPublicId = item.PublicId;
    }

    public Task DisposeAsync()
    {
        _client?.Dispose();
        _factory?.Dispose();
        foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            if (File.Exists(file)) File.Delete(file);
        }
        return Task.CompletedTask;
    }

    private string MintAssertion(string? sessionId)
    {
        using var scope = _factory.Services.CreateScope();
        var assertionService = scope.ServiceProvider.GetRequiredService<IMcpCallerAssertionService>();
        return assertionService.MintAssertion(_userId, _companyId, _slug, sessionId: sessionId);
    }

    private async Task<JsonNode> CallMcpToolAsync(string toolName, object? arguments = null, string? sessionId = "sess_deid_1")
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        req.Headers.Add(Api.Middleware.TenantResolutionMiddleware.McpServerKeyHeaderName, TestAnterApiKey);
        req.Headers.Add(McpCallerAssertionService.HeaderName, MintAssertion(sessionId));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var body = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Interlocked.Increment(ref _rpcId),
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = toolName,
                ["arguments"] = JsonNode.Parse(JsonSerializer.Serialize(arguments ?? new { })),
            }
        };
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var res = await _client.SendAsync(req);
        var raw = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{toolName} -> {(int)res.StatusCode}: {raw}");

        var json = raw.TrimStart().StartsWith('{')
            ? raw
            : string.Concat(raw.Split('\n').Where(l => l.StartsWith("data:")).Select(l => l["data:".Length..].Trim()));
        var rpc = JsonNode.Parse(json)!;
        Assert.Null(rpc["error"]);
        var result = rpc["result"]!;
        Assert.False(result["isError"]?.GetValue<bool>() ?? false, $"{toolName} returned isError: {raw}");

        var text = result["content"]?[0]?["text"]?.GetValue<string>();
        return text is not null ? JsonNode.Parse(text)! : result["structuredContent"]!;
    }

    [Fact]
    public async Task AllRegisteredMcpTools_AreCoveredByCentralCallToolFilter()
    {
        // Verify central CallToolFilter is registered on McpServerOptions.
        var mcpOptions = _factory.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        Assert.NotNull(mcpOptions.Filters.Request.CallToolFilters);
        Assert.NotEmpty(mcpOptions.Filters.Request.CallToolFilters);

        // Enumerate all tools from /mcp tools/list and verify all 6 tool groups are represented.
        using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        req.Headers.Add(Api.Middleware.TenantResolutionMiddleware.McpServerKeyHeaderName, TestAnterApiKey);
        req.Headers.Add(McpCallerAssertionService.HeaderName, MintAssertion("sess_enum"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        req.Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}}", Encoding.UTF8, "application/json");

        using var res = await _client.SendAsync(req);
        var raw = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, raw);
        var json = raw.TrimStart().StartsWith('{')
            ? raw
            : string.Concat(raw.Split('\n').Where(l => l.StartsWith("data:")).Select(l => l["data:".Length..].Trim()));
        var tools = JsonNode.Parse(json)!["result"]!["tools"]!.AsArray()
            .Select(t => t!["name"]!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(tools.Count >= 38, $"Expected at least 38 MCP tools, found {tools.Count}");
        Assert.Contains("search_clients", tools);
        Assert.Contains("search_vendors", tools);
        Assert.Contains("list_bank_accounts", tools);
        Assert.Contains("get_chart_of_accounts", tools);
        Assert.Contains("get_trial_balance", tools);
        Assert.Contains("get_members_and_roles", tools);
    }

    [Fact]
    public async Task AllMcpTools_LeakTest_And_FieldClassificationContractTest()
    {
        var sentinels = new[]
        {
            SentinelCustomerName,
            SentinelVendorName,
            SentinelBankName,
            SentinelEmail,
            SentinelPhone,
            SentinelAddress,
            SentinelIban,
            SentinelFreeText,
        };

        // Dynamically enumerate EVERY registered MCP tool from /mcp tools/list so no tool can be missed (D3).
        using var listReq = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        listReq.Headers.Add(Api.Middleware.TenantResolutionMiddleware.McpServerKeyHeaderName, TestAnterApiKey);
        listReq.Headers.Add(McpCallerAssertionService.HeaderName, MintAssertion("sess_leak_1"));
        listReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        listReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        listReq.Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}}", Encoding.UTF8, "application/json");
        using var listRes = await _client.SendAsync(listReq);
        var listRaw = await listRes.Content.ReadAsStringAsync();
        Assert.True(listRes.IsSuccessStatusCode, listRaw);
        var listJson = listRaw.TrimStart().StartsWith('{')
            ? listRaw
            : string.Concat(listRaw.Split('\n').Where(l => l.StartsWith("data:")).Select(l => l["data:".Length..].Trim()));
        var registeredTools = JsonNode.Parse(listJson)!["result"]!["tools"]!.AsArray()
            .Select(t => t!["name"]!.GetValue<string>())
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(registeredTools.Count >= 38, $"Expected at least 38 registered MCP tools, got {registeredTools.Count}");

        var specialArgs = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["get_client"] = new { clientId = _customerPublicId },
            ["get_invoice"] = new { invoiceId = _invoicePublicId },
            ["get_vendor"] = new { vendorId = _vendorPublicId },
            ["get_bill"] = new { billId = _billPublicId },
            ["get_bank_register"] = new { bankId = _bankPublicId },
            ["get_bank_reconciliations"] = new { bankId = _bankPublicId },
            ["get_bank_statement_lines"] = new { bankId = _bankPublicId },
            ["get_bank_outstanding_items"] = new { bankId = _bankPublicId },
            ["get_general_ledger_transactions"] = new { take = 25 },
            ["get_books_checks"] = new { fromDate = "2026-01-01", toDate = "2026-12-31" },
            ["get_record_history"] = new { entityType = "SalesInvoiceHeader", entityId = _invoicePublicId },
            ["get_journal_entry"] = new { journalEntryId = "je_00000000000000000000000000" },
            ["get_stock_movements"] = new { itemId = _itemPublicId },
        };

        var calls = registeredTools
            .Select(tool => (Tool: tool, Args: specialArgs.TryGetValue(tool, out var a) ? a : (object?)null))
            .ToList();
        calls.Add(("list_bank_accounts", new { bankId = _bankPublicId }));

        var calledTools = new HashSet<string>(StringComparer.Ordinal);
        var unclassifiedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string? customerTokenFromClients = null;
        string? customerTokenFromInvoices = null;
        string? vendorTokenFromVendors = null;
        string? bankTokenFromList = null;
        string? bankTokenFromCoa = null;

        foreach (var (tool, args) in calls)
        {
            calledTools.Add(tool);
            var payload = await CallMcpToolAsync(tool, args, sessionId: "sess_leak_1");
            var rawJson = payload.ToJsonString();

            // 1. Leak assertion: zero sentinel strings may appear anywhere.
            foreach (var sentinel in sentinels)
            {
                Assert.DoesNotContain(sentinel, rawJson, StringComparison.OrdinalIgnoreCase);
            }

            // 2. Contract assertion: every string property emitted must be explicitly classified.
            CollectUnclassifiedStringProperties(payload, unclassifiedProperties);

            if (tool == "search_clients")
            {
                customerTokenFromClients = payload["data"]![0]!["name"]!.GetValue<string>();
                Assert.Matches(@"^C-[0-9A-F]{8}$", customerTokenFromClients);
                Assert.Equal(McpFieldClassificationRegistry.MaskedPlaceholder, payload["data"]![0]!["email"]!.GetValue<string>());
                Assert.Equal(McpFieldClassificationRegistry.MaskedPlaceholder, payload["data"]![0]!["phone"]!.GetValue<string>());
            }
            else if (tool == "search_invoices")
            {
                customerTokenFromInvoices = payload["data"]![0]!["clientName"]!.GetValue<string>();
                Assert.Matches(@"^C-[0-9A-F]{8}$", customerTokenFromInvoices);
            }
            else if (tool == "search_vendors")
            {
                vendorTokenFromVendors = payload["data"]![0]!["name"]!.GetValue<string>();
                Assert.Matches(@"^V-[0-9A-F]{8}$", vendorTokenFromVendors);
            }
            else if (tool == "list_bank_accounts" && args is null)
            {
                var bankList = payload["data"] as JsonArray ?? payload["data"]?["items"] as JsonArray ?? payload["data"]?["accounts"] as JsonArray;
                Assert.True(bankList is not null && bankList.Count > 0, $"Unexpected list_bank_accounts payload: {rawJson}");
                bankTokenFromList = bankList![0]!["name"]!.GetValue<string>();
                Assert.Matches(@"^B-[0-9A-F]{8}$", bankTokenFromList);
            }
            else if (tool == "get_chart_of_accounts")
            {
                var bankAccountRow = payload["accounts"]!.AsArray()
                    .FirstOrDefault(a => a!["isCash"]?.GetValue<bool>() == true && a["name"]!.GetValue<string>().StartsWith("B-", StringComparison.Ordinal));
                Assert.NotNull(bankAccountRow);
                bankTokenFromCoa = bankAccountRow!["name"]!.GetValue<string>();
            }
            else if (tool == "get_bill")
            {
                var billData = payload["data"];
                Assert.True(billData is not null, $"get_bill returned null data: {rawJson}");
                var lineDesc = billData!["lines"]?[0]?["description"]?.GetValue<string>();
                Assert.True(lineDesc == McpFieldClassificationRegistry.HiddenTextPlaceholder, $"Unexpected get_bill payload: {rawJson}");
                Assert.Equal(McpFieldClassificationRegistry.MaskedPlaceholder, billData["vendorEmail"]?.GetValue<string>());
            }
        }

        // Consistent token across tools within the same session, and every registered tool was covered.
        Assert.Equal(registeredTools.Count, calledTools.Count);
        Assert.NotNull(customerTokenFromClients);
        Assert.Equal(customerTokenFromClients, customerTokenFromInvoices);
        Assert.NotNull(bankTokenFromList);
        Assert.Equal(bankTokenFromList, bankTokenFromCoa);

        Assert.True(unclassifiedProperties.Count == 0,
            "Unclassified MCP string properties found: " + string.Join(", ", unclassifiedProperties.OrderBy(x => x)));
    }

    [Fact]
    public async Task UnknownStringField_FailsClosedToMasked()
    {
        using var scope = _factory.Services.CreateScope();
        var deidentifier = scope.ServiceProvider.GetRequiredService<IMcpResponseDeidentifier>();
        var input = JsonNode.Parse("""{"status":"Success","totallyUnknownNewSecretField":"Sensitive Value 123"}""")!;

        var output = await deidentifier.DeidentifyJsonAsync("search_clients", input);
        Assert.Equal("Success", output!["status"]!.GetValue<string>());
        Assert.Equal(McpFieldClassificationRegistry.MaskedPlaceholder, output["totallyUnknownNewSecretField"]!.GetValue<string>());
    }

    [Fact]
    public async Task CfoNumericInvariance_AmountsAndTotalsAreIdenticalAfterDeidentification()
    {
        var invoiceList = await CallMcpToolAsync("search_invoices", null, sessionId: "sess_cfo_1");
        var firstInvoice = invoiceList["data"]!.AsArray().Single(i => i!["no"]!.GetValue<string>() == "INV-9001")!;
        Assert.Equal(251.00m, firstInvoice["total"]!.GetValue<decimal>());

        var billList = await CallMcpToolAsync("search_bills", null, sessionId: "sess_cfo_1");
        var firstBill = billList["data"]!.AsArray().Single(b => b!["no"]!.GetValue<string>() == "BILL-9001")!;
        Assert.Equal(88.25m, firstBill["total"]!.GetValue<decimal>());

        // Compare direct raw JSON vs de-identified JSON for TrialBalance & ReceivablesAging numbers.
        using var scope = _factory.Services.CreateScope();
        var deidentifier = scope.ServiceProvider.GetRequiredService<IMcpResponseDeidentifier>();
        var rawFinancial = JsonNode.Parse("""
        {
            "organization": "deid-org",
            "status": "Success",
            "totalDebit": 12345.67,
            "totalCredit": 12345.67,
            "difference": 0.00,
            "isBalanced": true,
            "items": [
                { "accountCode": "1010", "accountName": "Veridian Checking Sentinel", "debit": 12345.67, "credit": 0.00 }
            ]
        }
        """)!;
        var beforeNumbers = ExtractAllNumbers(rawFinancial);
        var deidentified = await deidentifier.DeidentifyJsonAsync("get_trial_balance", JsonNode.Parse(rawFinancial.ToJsonString())!);
        var afterNumbers = ExtractAllNumbers(deidentified!);

        Assert.Equal(beforeNumbers, afterNumbers);
    }

    [Fact]
    public async Task AttachmentMessageBuilder_PseudonymizesMatchedVendorConsistentlyWithMcpSession()
    {
        const string sessionId = "sess_attach_match_1";
        var vendorsRes = await CallMcpToolAsync("search_vendors", null, sessionId: sessionId);
        var expectedVendorToken = vendorsRes["data"]![0]!["name"]!.GetValue<string>();
        Assert.Matches(@"^V-[0-9A-F]{8}$", expectedVendorToken);

        using var scope = _factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>();
        using var _ = tenant.Impersonate(_companyId, _slug);
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var pseudonymService = scope.ServiceProvider.GetRequiredService<IAssistantPseudonymService>();

        var now = DateTime.UtcNow;
        var capture = new Core.Domain.DocumentCapture
        {
            CompanyId = _companyId,
            PublicId = "cap_attach01",
            Kind = "assistant-attachment",
            SchemaVersion = 1,
            Status = "accepted",
            SourceHash = "abc",
            ContentHash = "def",
            NormalizationVersion = "v1",
            ContentType = "application/pdf",
            ByteCount = 10,
            PageCount = 1,
            RequestFingerprint = "fp1",
            CreatedBy = _userId,
            CreatedAt = now,
            AcceptedBy = _userId,
            AcceptedAt = now,
            ExpiresAt = now.AddDays(7),
            AcceptedFieldsJson = $$"""
            {
              "fields": [
                { "label": "Vendor", "value": "{{SentinelVendorName}}" },
                { "label": "Customer", "value": "Unmatched Prospect Co" },
                { "label": "Total", "value": "88.25" }
              ]
            }
            """
        };
        db.DocumentCaptures.Add(capture);
        await db.SaveChangesAsync();

        var built = await AssistantAttachmentMessageBuilder.TryAppendAttachmentAsync(
            db, _companyId, _userId, capture.Id, "Please check this bill", CancellationToken.None, pseudonymService, sessionId);

        Assert.True(built.Succeeded);
        Assert.NotNull(built.CombinedMessage);
        Assert.DoesNotContain(SentinelVendorName, built.CombinedMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Unmatched Prospect Co", built.CombinedMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"- Vendor: {expectedVendorToken}", built.CombinedMessage!);
        Assert.Contains("- Total: 88.25", built.CombinedMessage!);
        var customerMatch = System.Text.RegularExpressions.Regex.Match(built.CombinedMessage!, @"- Customer: (C-[0-9A-F]{8})");
        Assert.True(customerMatch.Success);
        var syntheticCustomerToken = customerMatch.Groups[1].Value;

        // D1: Synthetic entry stores keyed syn: PublicId + DisplayText and re-identifies via /reidentify.
        var synRow = await db.AssistantPseudonyms.SingleAsync(r => r.SessionId == sessionId && r.Token == syntheticCustomerToken);
        Assert.StartsWith("syn:customer:", synRow.PublicId);
        Assert.Equal("Unmatched Prospect Co", synRow.DisplayText);

        using var reidReq = new HttpRequestMessage(HttpMethod.Post, $"/api/ai/chat/sessions/{sessionId}/reidentify");
        reidReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        reidReq.Headers.Add("X-AccountGo-Org", _slug);
        reidReq.Content = JsonContent.Create(new { tokens = new[] { expectedVendorToken, syntheticCustomerToken } });
        using var reidRes = await _client.SendAsync(reidReq);
        Assert.Equal(HttpStatusCode.OK, reidRes.StatusCode);
        using var reidDoc = JsonDocument.Parse(await reidRes.Content.ReadAsStringAsync());
        var names = reidDoc.RootElement.GetProperty("names");
        Assert.Equal(SentinelVendorName, names.GetProperty(expectedVendorToken).GetString());
        Assert.Equal("Unmatched Prospect Co", names.GetProperty(syntheticCustomerToken).GetString());

        // E1: Synthetic token passed to get_client is rejected as InvalidRequest and never echoes syn:.
        var synLookup = await CallMcpToolAsync("get_client", new { clientId = syntheticCustomerToken }, sessionId: sessionId);
        Assert.Equal("InvalidRequest", synLookup["status"]!.GetValue<string>());
        Assert.DoesNotContain("syn:", synLookup.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NoTenantFallback_UsesRandomPerRequestKey_NotUnkeyedHash()
    {
        using var scope1 = _factory.Services.CreateScope();
        using var scope2 = _factory.Services.CreateScope();
        var deid1 = scope1.ServiceProvider.GetRequiredService<IMcpResponseDeidentifier>();
        var deid2 = scope2.ServiceProvider.GetRequiredService<IMcpResponseDeidentifier>();

        var sample = """{"status":"Success","customerId":"cus_abc123","customerName":"Anonymous Fallback Customer"}""";
        var out1 = await deid1.DeidentifyJsonAsync("search_clients", JsonNode.Parse(sample)!);
        var out2 = await deid2.DeidentifyJsonAsync("search_clients", JsonNode.Parse(sample)!);

        var tok1 = out1!["customerName"]!.GetValue<string>();
        var tok2 = out2!["customerName"]!.GetValue<string>();
        Assert.Matches(@"^C-[0-9A-F]{8}$", tok1);
        Assert.Matches(@"^C-[0-9A-F]{8}$", tok2);
        Assert.NotEqual(tok1, tok2);
    }

    [Fact]
    public async Task ResolveIdAsync_WhenMappedEntityDeleted_EchoesOnlyOriginalTokenNotPublicId()
    {
        const string sessionId = "sess_deleted_echo";
        string deletedPublicId;
        string ghostToken;
        using (var scope = _factory.Services.CreateScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>();
            using var _ = tenant.Impersonate(_companyId, _slug);
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var tempCustomer = new Customer
            {
                CompanyId = _companyId,
                No = "CUST-TEMP-DEL",
                Party = new Party
                {
                    CompanyId = _companyId,
                    Name = "Temporary Ghost Customer",
                    PartyType = PartyTypes.Customer,
                    IsActive = true,
                }
            };
            db.Customers.Add(tempCustomer);
            await db.SaveChangesAsync();
            deletedPublicId = tempCustomer.PublicId;

            var svc = scope.ServiceProvider.GetRequiredService<IAssistantPseudonymService>();
            ghostToken = await svc.PseudonymizeAsync(_companyId, _userId, sessionId, AssistantPseudonymKind.Customer, deletedPublicId);

            db.Customers.Remove(tempCustomer);
            await db.SaveChangesAsync();
        }

        var notFound = await CallMcpToolAsync("get_client", new { clientId = ghostToken }, sessionId: sessionId);
        Assert.Equal("NotFound", notFound["status"]!.GetValue<string>());
        var message = notFound["message"]!.GetValue<string>();
        Assert.Contains(ghostToken, message);
        Assert.DoesNotContain(deletedPublicId, message);
    }

    [Fact]
    public async Task ToolInputs_AcceptSessionScopedPseudonymTokens_AndRejectUnknownCrossSessionOrWrongKind()
    {
        const string sessionA = "sess_input_A";
        const string sessionB = "sess_input_B";

        var clientsA = await CallMcpToolAsync("search_clients", null, sessionId: sessionA);
        var customerTokenA = clientsA["data"]![0]!["name"]!.GetValue<string>();
        Assert.Matches(@"^C-[0-9A-F]{8}$", customerTokenA);

        var vendorsA = await CallMcpToolAsync("search_vendors", null, sessionId: sessionA);
        var vendorTokenA = vendorsA["data"]![0]!["name"]!.GetValue<string>();
        Assert.Matches(@"^V-[0-9A-F]{8}$", vendorTokenA);

        var banksA = await CallMcpToolAsync("list_bank_accounts", null, sessionId: sessionA);
        var bankListA = banksA["data"] as JsonArray ?? banksA["data"]?["items"] as JsonArray ?? banksA["data"]?["accounts"] as JsonArray;
        var bankTokenA = bankListA![0]!["name"]!.GetValue<string>();
        Assert.Matches(@"^B-[0-9A-F]{8}$", bankTokenA);

        // Same session: pseudonym tokens resolve and return matching records.
        var clientRes = await CallMcpToolAsync("get_client", new { clientId = customerTokenA }, sessionId: sessionA);
        Assert.Equal("Success", clientRes["status"]!.GetValue<string>());
        Assert.Equal(_customerPublicId, clientRes["data"]!["client"]!["id"]!.GetValue<string>());

        var invoicesByToken = await CallMcpToolAsync("search_invoices", new { clientId = customerTokenA }, sessionId: sessionA);
        Assert.Equal("Success", invoicesByToken["status"]!.GetValue<string>());
        Assert.Single(invoicesByToken["data"]!.AsArray());

        var vendorRes = await CallMcpToolAsync("get_vendor", new { vendorId = vendorTokenA }, sessionId: sessionA);
        Assert.Equal("Success", vendorRes["status"]!.GetValue<string>());
        Assert.Equal(_vendorPublicId, vendorRes["data"]!["vendor"]!["id"]!.GetValue<string>());

        var billsByToken = await CallMcpToolAsync("search_bills", new { vendorId = vendorTokenA }, sessionId: sessionA);
        Assert.Equal("Success", billsByToken["status"]!.GetValue<string>());
        Assert.Single(billsByToken["data"]!.AsArray());

        var bankRes = await CallMcpToolAsync("list_bank_accounts", new { bankId = bankTokenA }, sessionId: sessionA);
        Assert.Equal("Success", bankRes["status"]!.GetValue<string>());

        // Cross-session token -> InvalidRequest (existing invalid-id response).
        var crossSession = await CallMcpToolAsync("get_client", new { clientId = customerTokenA }, sessionId: sessionB);
        Assert.Equal("InvalidRequest", crossSession["status"]!.GetValue<string>());

        // Unknown token -> InvalidRequest.
        var unknown = await CallMcpToolAsync("get_client", new { clientId = "C-DEADBEEF" }, sessionId: sessionA);
        Assert.Equal("InvalidRequest", unknown["status"]!.GetValue<string>());

        // Wrong kind (vendor token passed to get_client) -> InvalidRequest.
        var wrongKind = await CallMcpToolAsync("get_client", new { clientId = vendorTokenA }, sessionId: sessionA);
        Assert.Equal("InvalidRequest", wrongKind["status"]!.GetValue<string>());

        // Absent sessionId (ephemeral mode) -> token cannot resolve -> InvalidRequest.
        var noSession = await CallMcpToolAsync("get_client", new { clientId = customerTokenA }, sessionId: null);
        Assert.Equal("InvalidRequest", noSession["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task ReidentifyEndpoint_ResolvesAuthorizedTokens_EnforcesPermissionsScopeAnd200Cap()
    {
        const string sessionId = "sess_reid_1";
        var clientsRes = await CallMcpToolAsync("search_clients", null, sessionId: sessionId);
        var customerToken = clientsRes["data"]![0]!["name"]!.GetValue<string>();
        var vendorsRes = await CallMcpToolAsync("search_vendors", null, sessionId: sessionId);
        var vendorToken = vendorsRes["data"]![0]!["name"]!.GetValue<string>();
        var banksRes = await CallMcpToolAsync("list_bank_accounts", null, sessionId: sessionId);
        var bankList = banksRes["data"] as JsonArray ?? banksRes["data"]?["items"] as JsonArray ?? banksRes["data"]?["accounts"] as JsonArray;
        var bankToken = bankList![0]!["name"]!.GetValue<string>();

        // 1. Valid tokens resolve to real names; unknown token is omitted.
        using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/ai/chat/sessions/{sessionId}/reidentify"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            req.Headers.Add("X-AccountGo-Org", _slug);
            req.Content = JsonContent.Create(new { tokens = new[] { customerToken, vendorToken, bankToken, "C-DEADBEEF" } });

            using var res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var names = doc.RootElement.GetProperty("names");
            Assert.Equal(SentinelCustomerName, names.GetProperty(customerToken).GetString());
            Assert.Equal(SentinelVendorName, names.GetProperty(vendorToken).GetString());
            Assert.Equal(SentinelBankName, names.GetProperty(bankToken).GetString());
            Assert.False(names.TryGetProperty("C-DEADBEEF", out _));
        }

        // 2. Cross-session request omits tokens from another session.
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/ai/chat/sessions/sess_reid_other/reidentify"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            req.Headers.Add("X-AccountGo-Org", _slug);
            req.Content = JsonContent.Create(new { tokens = new[] { customerToken, vendorToken } });

            using var res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            Assert.Empty(doc.RootElement.GetProperty("names").EnumerateObject());
        }

        // 3. More than 200 tokens -> 400 BadRequest.
        using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/ai/chat/sessions/{sessionId}/reidentify"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            req.Headers.Add("X-AccountGo-Org", _slug);
            var tooMany = Enumerable.Range(0, 201).Select(i => $"C-{i:X8}").ToArray();
            req.Content = JsonContent.Create(new { tokens = tooMany });

            using var res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        // 4. Cross-workspace request omits tokens from workspace A.
        var otherSlug = "deid-org-b-" + Guid.NewGuid().ToString("N")[..8];
        using (var createOrgReq = new HttpRequestMessage(HttpMethod.Post, "/api/organizations"))
        {
            createOrgReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            createOrgReq.Content = JsonContent.Create(new { name = "Deid Other Org", slug = otherSlug });
            var createOrgRes = await _client.SendAsync(createOrgReq);
            Assert.Equal(HttpStatusCode.Created, createOrgRes.StatusCode);
        }

        using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/ai/chat/sessions/{sessionId}/reidentify"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            req.Headers.Add("X-AccountGo-Org", otherSlug);
            req.Content = JsonContent.Create(new { tokens = new[] { customerToken } });

            using var res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            Assert.Empty(doc.RootElement.GetProperty("names").EnumerateObject());
        }

        // 5. Revoking functional read permissions omits forbidden entity tokens.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var ownerMember = await db.CompanyMembers.SingleAsync(m => m.CompanyId == _companyId && m.UserId == _userId);
            var roles = await db.CompanyMemberSecurityRoles.Where(r => r.CompanyMemberId == ownerMember.Id).ToListAsync();
            db.CompanyMemberSecurityRoles.RemoveRange(roles);
            await db.SaveChangesAsync();
        }

        using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/ai/chat/sessions/{sessionId}/reidentify"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            req.Headers.Add("X-AccountGo-Org", _slug);
            req.Content = JsonContent.Create(new { tokens = new[] { customerToken, vendorToken, bankToken } });

            using var res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            Assert.Empty(doc.RootElement.GetProperty("names").EnumerateObject());
        }
    }

    private static void CollectUnclassifiedStringProperties(JsonNode node, HashSet<string> unclassified)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var kvp in obj)
                {
                    if (kvp.Value is null) continue;
                    if (kvp.Value is JsonValue val && val.TryGetValue<string>(out _))
                    {
                        if (!McpFieldClassificationRegistry.IsExplicitlyClassified(kvp.Key))
                        {
                            unclassified.Add(kvp.Key);
                        }
                    }
                    else
                    {
                        CollectUnclassifiedStringProperties(kvp.Value, unclassified);
                    }
                }
                break;
            case JsonArray arr:
                foreach (var item in arr)
                {
                    if (item is not null) CollectUnclassifiedStringProperties(item, unclassified);
                }
                break;
        }
    }

    private static List<(string Path, decimal Value)> ExtractAllNumbers(JsonNode node, string path = "$")
    {
        var list = new List<(string, decimal)>();
        switch (node)
        {
            case JsonObject obj:
                foreach (var kvp in obj.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    if (kvp.Value is not null)
                        list.AddRange(ExtractAllNumbers(kvp.Value, $"{path}.{kvp.Key}"));
                }
                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is not null)
                        list.AddRange(ExtractAllNumbers(arr[i]!, $"{path}[{i}]"));
                }
                break;
            case JsonValue val when val.TryGetValue<decimal>(out var d):
                list.Add((path, d));
                break;
        }
        return list;
    }
}
