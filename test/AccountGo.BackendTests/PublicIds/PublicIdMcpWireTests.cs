// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace AccountGo.BackendTests.PublicIds;

/// <summary>
/// Calls the assistant (MCP) tools through the real endpoint on demo data and walks every result: no tool may return
/// an integer record key, every id-taking tool accepts the public ids it returned, and a wrong-kind, numeric or
/// unknown id is refused with a clear status instead of an error.
/// </summary>
[Collection("WorkspaceSetupHttp")]
public sealed class PublicIdMcpWireTests
{
    private static int _rpcId = 100;

    private static async Task<JsonNode?> CallAsync(HttpClient client, string token, string slug, string tool, object? arguments = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("X-AccountGo-Org", slug);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var body = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Interlocked.Increment(ref _rpcId),
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = tool,
                ["arguments"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(arguments ?? new { }))
            }
        };
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var res = await client.SendAsync(req);
        var raw = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{tool} -> {(int)res.StatusCode}: {raw}");

        // The endpoint answers with JSON or a server-sent event; take the JSON-RPC message either way.
        var json = raw.TrimStart().StartsWith('{')
            ? raw
            : string.Concat(raw.Split('\n').Where(l => l.StartsWith("data:")).Select(l => l["data:".Length..].Trim()));
        var rpc = JsonNode.Parse(json)!;
        Assert.Null(rpc["error"]);
        var result = rpc["result"]!;
        Assert.False(result["isError"]?.GetValue<bool>() ?? false, $"{tool} failed: {raw}");

        // Walk the structured result and the text the model reads: both must be free of integer keys.
        var text = result["content"]?[0]?["text"]?.GetValue<string>();
        var parsed = text != null ? JsonNode.Parse(text) : result["structuredContent"];
        // The record history tool returns the same rows as the Activity page, whose audit row ids are documented exemptions.
        var wireName = tool == "get_record_history" ? "/api/organizations/current/activity" : "/mcp:" + tool;
        PublicIdHttpWireTests.AssertNoNumericIdsOnWire(result["structuredContent"], wireName);
        PublicIdHttpWireTests.AssertNoNumericIdsOnWire(parsed, wireName);
        return parsed;
    }

    private static string? FirstId(JsonNode? response, string field = "id")
    {
        var data = response?["data"];
        var first = data is JsonArray array ? array.FirstOrDefault() : data?["items"] is JsonArray items ? items.FirstOrDefault() : data;
        return first?[field]?.GetValue<string>();
    }

    [Fact]
    public async Task McpTools_ReturnOnlyPublicIds_AndAcceptThemBack()
    {
        var (factory, client, token, org, _, sqlitePath) = await PublicIdHttpWireTests.CreateHostAsync();
        try
        {
            // Lists and reports that take no ids.
            foreach (var tool in new[]
                     {
                         "get_workspace_context", "get_dashboard_summary", "search_clients", "search_vendors", "search_invoices",
                         "search_bills", "search_bill_payments", "get_receivables_aging", "get_payables_aging", "search_journal_entries",
                         "search_items", "get_tax_rates", "list_bank_accounts", "get_chart_of_accounts", "get_trial_balance",
                         "get_balance_sheet", "get_income_statement", "get_cash_flow", "get_fiscal_years", "get_members_and_roles",
                         "get_equity_statement", "get_tax_report", "search_receipts", "get_record_history", "get_recurring_schedules",
                     })
                await CallAsync(client, token, org, tool);

            await CallAsync(client, token, org, "get_general_ledger_transactions", new { take = 25 });
            await CallAsync(client, token, org, "get_books_checks", new { fromDate = "2026-01-01", toDate = "2026-12-31" });
            await CallAsync(client, token, org, "get_receivables_aging", new { asOfDate = "2026-12-31" });
            await CallAsync(client, token, org, "get_payables_aging", new { asOfDate = "2026-12-31" });

            // Ids returned by the lists must work in the tools that take them.
            var clientId = FirstId(await CallAsync(client, token, org, "search_clients"));
            var vendorId = FirstId(await CallAsync(client, token, org, "search_vendors"));
            var invoiceId = FirstId(await CallAsync(client, token, org, "search_invoices"));
            var billId = FirstId(await CallAsync(client, token, org, "search_bills"));
            var journalId = FirstId(await CallAsync(client, token, org, "search_journal_entries"));
            var itemId = FirstId(await CallAsync(client, token, org, "search_items"));
            var banks = await CallAsync(client, token, org, "list_bank_accounts");
            var bankId = FirstId(banks);

            if (clientId != null) { Assert.StartsWith("cus_", clientId); await CallAsync(client, token, org, "get_client", new { clientId }); await CallAsync(client, token, org, "search_invoices", new { clientId }); }
            if (vendorId != null) { Assert.StartsWith("ven_", vendorId); await CallAsync(client, token, org, "get_vendor", new { vendorId }); await CallAsync(client, token, org, "search_bills", new { vendorId }); }
            if (invoiceId != null) { Assert.StartsWith("inv_", invoiceId); await CallAsync(client, token, org, "get_invoice", new { invoiceId }); }
            if (billId != null) { Assert.StartsWith("bill_", billId); await CallAsync(client, token, org, "get_bill", new { billId }); }
            if (journalId != null) { Assert.StartsWith("je_", journalId); await CallAsync(client, token, org, "get_journal_entry", new { journalEntryId = journalId }); }
            if (itemId != null) { Assert.StartsWith("item_", itemId); await CallAsync(client, token, org, "search_items", new { itemId }); await CallAsync(client, token, org, "get_stock_movements", new { itemId }); }
            if (bankId != null)
            {
                Assert.StartsWith("ba_", bankId);
                await CallAsync(client, token, org, "list_bank_accounts", new { bankId });
                await CallAsync(client, token, org, "get_bank_register", new { bankId });
                await CallAsync(client, token, org, "get_bank_reconciliations", new { bankId });
                await CallAsync(client, token, org, "get_bank_outstanding_items", new { bankId });
                await CallAsync(client, token, org, "get_bank_statement_lines", new { bankId });
            }

            // Record history takes the public id of the record and its type.
            if (invoiceId != null)
                await CallAsync(client, token, org, "get_record_history", new { entityType = "SalesInvoiceHeader", entityId = invoiceId });

            // Refusals: wrong kind, numeric and unknown ids come back as a status the assistant can explain.
            if (clientId != null && invoiceId != null)
            {
                var wrongKind = await CallAsync(client, token, org, "get_invoice", new { invoiceId = clientId });
                Assert.Equal("InvalidRequest", wrongKind!["status"]!.GetValue<string>());
            }
            var numeric = await CallAsync(client, token, org, "get_invoice", new { invoiceId = "1" });
            Assert.Equal("InvalidRequest", numeric!["status"]!.GetValue<string>());
            var unknown = await CallAsync(client, token, org, "get_invoice", new { invoiceId = "inv_00000000000000000000000000" });
            Assert.Equal("NotFound", unknown!["status"]!.GetValue<string>());
        }
        finally
        {
            client.Dispose();
            await factory.DisposeAsync();
            if (File.Exists(sqlitePath)) File.Delete(sqlitePath);
        }
    }
}
