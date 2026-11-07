#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Dto.Banking;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests
{
    /// <summary>
    /// The assistant's data tools read the same GET actions the pages use, behind the same filters, for the member the
    /// Anter run is for. These tests call them the way Anter does: the shared gate key plus a signed caller assertion.
    /// </summary>
    public partial class MultiTenancyPhase3Tests
    {
        internal const string TestAnterApiKey = "proj_api-key_phase3_anter_mcp_gate_secret";

        private static readonly string[] AssistantDataTools =
        {
            "get_workspace_context", "get_dashboard_summary", "search_journal_entries", "get_journal_entry", "search_items",
            "get_tax_rates", "get_linked_entries", "search_clients", "get_client", "search_invoices", "get_invoice",
            "get_receivables_aging", "get_recurring_schedules", "search_vendors", "get_vendor", "search_bills", "get_bill",
            "get_payables_aging", "list_bank_accounts", "get_bank_register", "get_bank_reconciliations", "get_bank_statement_lines"
        };

        [Fact]
        public async Task AssistantTools_AreListedAsReadOnly()
        {
            var response = await McpRequest(new { jsonrpc = "2.0", id = 1, method = "tools/list", @params = new { } });
            var tools = response.GetProperty("result").GetProperty("tools").EnumerateArray()
                .ToDictionary(t => t.GetProperty("name").GetString());

            foreach (var name in AssistantDataTools)
            {
                Assert.True(tools.ContainsKey(name), $"{name} is not listed");
                var annotations = tools[name].GetProperty("annotations");
                Assert.True(annotations.GetProperty("readOnlyHint").GetBoolean(), $"{name} must be read-only");
                Assert.False(annotations.GetProperty("destructiveHint").GetBoolean(), $"{name} must not be destructive");
            }
        }

        [Fact]
        public async Task AssistantTools_FollowAPostedInvoiceFromTheLedgerToItsDetail()
        {
            var invoice = await NewInvoice();
            invoice.Status = "Open";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var invoiceId = (await Json(saved)).GetProperty("id").GetInt32();
            var invoiceNo = (await GetInvoice(invoiceId)).No;
            Assert.False(string.IsNullOrEmpty(invoiceNo));

            var ledger = await CallTool("get_general_ledger_transactions", new { documentType = "SalesInvoice" });
            var sourceId = ledger.GetProperty("transactions").EnumerateArray()
                .Select(t => t.GetProperty("sourceDocumentId").GetInt32()).Single();
            Assert.Equal(invoiceId, sourceId);

            var byId = await CallTool("get_invoice", new { invoiceId = sourceId });
            Assert.Equal("Success", byId.GetProperty("status").GetString());
            Assert.Equal($"/org/{_slug}/sales/{invoiceId}", byId.GetProperty("link").GetString());
            var data = byId.GetProperty("data");
            Assert.Equal(invoiceNo, data.GetProperty("no").GetString());
            Assert.Equal("Real customer", data.GetProperty("customerName").GetString());
            Assert.Equal(100m, data.GetProperty("balanceDue").GetDecimal());
            Assert.Single(data.GetProperty("salesInvoiceLines").EnumerateArray());

            var byNumber = await CallTool("get_invoice", new { invoiceNo });
            Assert.Equal(invoiceId, byNumber.GetProperty("data").GetProperty("id").GetInt32());

            var missing = await CallTool("get_invoice", new { invoiceNo = "NOPE-1" });
            Assert.Equal("NotFound", missing.GetProperty("status").GetString());

            var search = await CallTool("search_invoices", new { query = invoiceNo });
            Assert.Equal(1, search.GetProperty("totalCount").GetInt32());
            var row = search.GetProperty("data").EnumerateArray().Single();
            Assert.Equal("Open", row.GetProperty("status").GetString());
            Assert.Equal(0, (await CallTool("search_invoices", new { status = "Paid" })).GetProperty("totalCount").GetInt32());

            var clientId = data.GetProperty("customerId").GetInt32();
            var client = await CallTool("get_client", new { clientId });
            Assert.Equal(100m, client.GetProperty("data").GetProperty("client").GetProperty("outstanding").GetDecimal());
            Assert.Equal(1, client.GetProperty("data").GetProperty("recentInvoices").GetProperty("totalCount").GetInt32());

            var aging = (await CallTool("get_receivables_aging", new { })).GetProperty("data");
            Assert.Equal(100m, aging.GetProperty("total").GetDecimal());
            Assert.Equal(100m, aging.GetProperty("buckets").GetProperty("current").GetDecimal());
            Assert.Equal(clientId, aging.GetProperty("parties").EnumerateArray().Single().GetProperty("id").GetInt32());
        }

        [Fact]
        public async Task AssistantTools_ReadBillsVendorsAndPayables()
        {
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Assistant supplier" });
            var bill = NewBill(vendor.Id, ("Assistant expense", 2, 60m, null));
            bill.VendorInvoiceNo = "SUP-778";
            bill.Status = "Open";
            var approved = await SaveBillOk(bill);

            var byVendorNumber = await CallTool("get_bill", new { billNo = "SUP-778" });
            Assert.Equal("Success", byVendorNumber.GetProperty("status").GetString());
            Assert.Equal(approved.Id, byVendorNumber.GetProperty("data").GetProperty("id").GetInt32());
            Assert.Equal(120m, byVendorNumber.GetProperty("data").GetProperty("balanceDue").GetDecimal());

            var bills = await CallTool("search_bills", new { vendorId = vendor.Id });
            Assert.Equal(1, bills.GetProperty("totalCount").GetInt32());

            var vendors = await CallTool("search_vendors", new { query = "assistant", withBalanceOnly = true });
            Assert.Equal(vendor.Id, vendors.GetProperty("data").EnumerateArray().Single().GetProperty("id").GetInt32());

            var detail = await CallTool("get_vendor", new { vendorId = vendor.Id });
            Assert.Equal(1, detail.GetProperty("data").GetProperty("recentBills").GetProperty("totalCount").GetInt32());

            var aging = (await CallTool("get_payables_aging", new { })).GetProperty("data");
            Assert.Equal(120m, aging.GetProperty("total").GetDecimal());
        }

        [Fact]
        public async Task AssistantTools_ContextBankingAndCatalog_NeverExposeFullBankNumbers()
        {
            var bank = await CreateBankOk(new CreateBankAccountRequest
            {
                Name = "Assistant checking", BankName = "Example Bank", AccountNumber = "123456789012", RoutingNumber = "021000021",
                Iban = "GB33BUKB20201555555555"
            });

            var list = await CallTool("list_bank_accounts", new { });
            Assert.Equal("Success", list.GetProperty("status").GetString());
            var raw = list.GetRawText();
            Assert.DoesNotContain("123456789012", raw);
            Assert.DoesNotContain("021000021", raw);
            Assert.DoesNotContain("GB33BUKB20201555555555", raw);
            Assert.Contains("Assistant checking", raw);

            var one = await CallTool("list_bank_accounts", new { bankId = bank.Id });
            Assert.DoesNotContain("123456789012", one.GetRawText());

            var register = await CallTool("get_bank_register", new { bankId = bank.Id });
            Assert.Equal("Success", register.GetProperty("status").GetString());

            var context = await CallTool("get_workspace_context", new { });
            Assert.Equal("Success", context.GetProperty("status").GetString());
            var workspace = context.GetProperty("data").GetProperty("workspace");
            Assert.Equal(_slug, workspace.GetProperty("slug").GetString());
            Assert.NotEmpty(workspace.GetProperty("permissions").EnumerateArray());

            foreach (var tool in new[] { "get_dashboard_summary", "search_journal_entries", "search_items", "get_tax_rates", "get_recurring_schedules" })
            {
                var result = await CallTool(tool, new { });
                Assert.True(result.GetProperty("status").GetString() == "Success", $"{tool}: {result.GetRawText()}");
            }

            var linked = await CallTool("get_linked_entries", new { });
            Assert.Equal("Success", linked.GetProperty("status").GetString());
        }

        [Fact]
        public async Task AssistantTools_MemberWithoutSalesPermissions_IsRefusedNotErrored()
        {
            await JoinOther();

            var invoices = await CallTool("search_invoices", new { }, _otherId);
            Assert.Equal("AccessDenied", invoices.GetProperty("status").GetString());
            Assert.False(invoices.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null);

            var linked = await CallTool("get_linked_entries", new { }, _otherId);
            Assert.Equal("AccessDenied", linked.GetProperty("status").GetString());

            var context = await CallTool("get_workspace_context", new { }, _otherId);
            Assert.Equal("Success", context.GetProperty("status").GetString());
            Assert.Equal("Member", context.GetProperty("data").GetProperty("workspace").GetProperty("role").GetString());
        }

        /// <summary>Calls a tool as Anter does and returns its structured result.</summary>
        private async Task<JsonElement> CallTool(string name, object arguments, string userId = null)
        {
            var response = await McpRequest(
                new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name, arguments } }, userId);
            Assert.True(response.TryGetProperty("result", out var result), response.GetRawText());
            Assert.False(result.TryGetProperty("isError", out var isError) && isError.GetBoolean(), result.GetRawText());
            var text = result.GetProperty("content").EnumerateArray().First().GetProperty("text").GetString();
            return JsonDocument.Parse(text).RootElement.Clone();
        }

        private async Task<JsonElement> McpRequest(object body, string userId = null)
        {
            string assertion;
            using (var scope = _factory.Services.CreateScope())
            {
                assertion = scope.ServiceProvider.GetRequiredService<Api.Service.Ai.IMcpCallerAssertionService>()
                    .MintAssertion(userId ?? _ownerId, _companyId, _slug);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestAnterApiKey);
            request.Headers.Add(Api.Service.Ai.McpCallerAssertionService.HeaderName, assertion);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");

            var payload = text.TrimStart().StartsWith("{", StringComparison.Ordinal)
                ? text
                : text.Split('\n').Select(l => l.TrimEnd('\r')).Last(l => l.StartsWith("data: ", StringComparison.Ordinal))["data: ".Length..];
            return JsonDocument.Parse(payload).RootElement.Clone();
        }
    }
}
