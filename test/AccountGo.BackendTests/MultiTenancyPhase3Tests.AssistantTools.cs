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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace AccountGo.BackendTests
{
    /// <summary>
    /// The assistant's data tools read the same GET actions the pages use, behind the same filters, for the member the
    /// Anter run is for. These tests call them the way Anter does: the shared gate key plus a signed caller assertion.
    /// </summary>
    public abstract partial class MultiTenancyPhase3TestBase
    {
        internal const string TestAnterApiKey = "proj_api-key_phase3_anter_mcp_gate_secret";

        protected static readonly string[] AssistantDataTools =
        {
            "get_workspace_context", "get_dashboard_summary", "get_books_checks", "search_journal_entries", "get_journal_entry", "search_items",
            "get_tax_rates", "get_linked_entries", "search_clients", "get_client", "search_invoices", "get_invoice",
            "get_receivables_aging", "get_recurring_schedules", "search_vendors", "get_vendor", "search_bills", "get_bill",
            "get_payables_aging", "search_bill_payments", "list_bank_accounts", "get_bank_register", "get_bank_reconciliations", "get_bank_statement_lines"
        };

        /// <summary>Calls a tool as Anter does and returns its structured result.</summary>
        protected async Task<JsonElement> CallTool(string name, object arguments, string userId = null)
        {
            var response = await McpRequest(
                new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name, arguments } }, userId);
            Assert.True(response.TryGetProperty("result", out var result), response.GetRawText());
            Assert.False(result.TryGetProperty("isError", out var isError) && isError.GetBoolean(), result.GetRawText());
            var text = result.GetProperty("content").EnumerateArray().First().GetProperty("text").GetString();
            return JsonDocument.Parse(text).RootElement.Clone();
        }

        protected async Task<JsonElement> McpRequest(object body, string userId = null)
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

    public sealed class MultiTenancyPhase3AssistantToolsTests : MultiTenancyPhase3TestBase
    {
        public MultiTenancyPhase3AssistantToolsTests(ITestOutputHelper output) : base(output) { }

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
        public async Task AssistantTools_SelectJournalsByAmountAndText_AndShowWhoPreparedAndPosted()
        {
            var today = DateTime.UtcNow.Date;
            await PostJournal(today, ("10120", 1, 50m), ("40100", 2, 50m));
            await PostJournal(today, ("10120", 1, 5000m), ("40100", 2, 5000m));

            async Task<List<JsonElement>> Journals(object arguments) =>
                (await CallTool("get_general_ledger_transactions", arguments)).GetProperty("transactions").EnumerateArray().ToList();

            Assert.Equal(2, (await Journals(new { documentType = "JournalEntry" })).Count);
            var large = Assert.Single(await Journals(new { documentType = "JournalEntry", minAmount = 1000 }));
            Assert.Equal(5000m, large.GetProperty("totalDebit").GetDecimal());
            var small = Assert.Single(await Journals(new { documentType = "JournalEntry", maxAmount = 100 }));
            Assert.Equal(50m, small.GetProperty("totalDebit").GetDecimal());
            Assert.Equal(2, (await Journals(new { search = "DASHBOARD period" })).Count);
            Assert.Empty(await Journals(new { search = "no such narration" }));

            var bad = await McpRequest(new { jsonrpc = "2.0", id = 4, method = "tools/call",
                @params = new { name = "get_general_ledger_transactions", arguments = new { minAmount = 10, maxAmount = 5 } } });
            Assert.True(bad.GetProperty("result").GetProperty("isError").GetBoolean());

            // Who prepared and posted is record history: hidden until the member may view it.
            var journalId = large.GetProperty("sourceDocumentId").GetInt32();
            var hidden = (await CallTool("get_journal_entry", new { journalEntryId = journalId })).GetProperty("data");
            Assert.Equal(JsonValueKind.Null, hidden.GetProperty("attribution").ValueKind);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Api.Data.ApiDbContext>();
                var ownerMemberId = db.CompanyMembers.IgnoreQueryFilters().Single(m => m.CompanyId == _companyId && m.UserId == _ownerId).Id;
                var viewerRoleId = db.SecurityRoles.IgnoreQueryFilters().Single(r => r.Name == "Auditor / Viewer").Id;
                db.CompanyMemberSecurityRoles.Add(new Core.Domain.CompanyMemberSecurityRole { CompanyMemberId = ownerMemberId, SecurityRoleId = viewerRoleId });
                await db.SaveChangesAsync();
            }

            var journal = (await CallTool("get_journal_entry", new { journalEntryId = journalId })).GetProperty("data");
            var attribution = journal.GetProperty("attribution");
            var preparer = Assert.Single(attribution.GetProperty("preparedBy").EnumerateArray());
            Assert.Equal(_ownerId, preparer.GetProperty("userId").GetString());
            Assert.Equal(_ownerId, attribution.GetProperty("postedByUserId").GetString());
            Assert.True(attribution.GetProperty("postedByAPreparer").GetBoolean());
            Assert.True(attribution.GetProperty("postedAt").GetDateTime() > DateTime.UtcNow.AddMinutes(-30));
        }

        [Fact]
        public async Task AssistantTools_ReceivablesAgingAsOfADate_FollowsLedgerDatesAndTiesToTheControlAccount()
        {
            var today = DateTime.UtcNow.Date;
            string Iso(DateTime d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

            var invoice = await NewInvoice();
            invoice.InvoiceDate = today.AddDays(-2);
            invoice.Status = "Open";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var invoiceId = (await Json(saved)).GetProperty("id").GetInt32();

            var bank = await CreateBankOk(new CreateBankAccountRequest { Name = "Aging bank", Type = 1 });
            var paid = await Send(HttpMethod.Post, "/api/Sales/RecordPayment",
                new Dto.Sales.RecordInvoicePaymentDto { InvoiceId = invoiceId, Amount = 40, PaymentDate = today, BankAccountId = bank.Id });
            Assert.True(paid.IsSuccessStatusCode, await paid.Content.ReadAsStringAsync());

            // Before the invoice existed nothing was owed.
            var before = (await CallTool("get_receivables_aging", new { asOfDate = Iso(today.AddDays(-3)) })).GetProperty("data");
            Assert.Equal(0m, before.GetProperty("total").GetDecimal());

            // The day before the receipt the whole invoice was owed, although today only 60 is.
            var dayBefore = (await CallTool("get_receivables_aging", new { asOfDate = Iso(today.AddDays(-1)) })).GetProperty("data");
            Assert.Equal(100m, dayBefore.GetProperty("total").GetDecimal());
            Assert.Equal(0m, dayBefore.GetProperty("unpostedTotal").GetDecimal());
            Assert.Equal(today.AddDays(-1), dayBefore.GetProperty("asOf").GetDateTime().Date);

            var now = (await CallTool("get_receivables_aging", new { asOfDate = Iso(today) })).GetProperty("data");
            Assert.Equal(60m, now.GetProperty("total").GetDecimal());
            Assert.Equal(60m, (await CallTool("get_receivables_aging", new { })).GetProperty("data").GetProperty("total").GetDecimal());

            // Each as-of total equals the receivables account in the balance sheet at the same date.
            foreach (var (date, expected) in new[] { (today.AddDays(-1), 100m), (today, 60m) })
            {
                var sheet = await CallTool("get_balance_sheet", new { asOfDate = Iso(date) });
                var receivable = sheet.GetProperty("items").EnumerateArray()
                    .Where(i => i.GetProperty("accountCode").GetString() == "10120")
                    .Sum(i => i.GetProperty("amount").GetDecimal());
                Assert.Equal(expected, receivable);
            }

            var bad = await McpRequest(new { jsonrpc = "2.0", id = 3, method = "tools/call",
                @params = new { name = "get_receivables_aging", arguments = new { asOfDate = "not-a-date" } } });
            Assert.True(bad.GetProperty("result").GetProperty("isError").GetBoolean());
        }

        [Fact]
        public async Task AssistantTools_PayablesAgingAsOfADate_FollowsPaymentAndVoidDates()
        {
            var today = DateTime.UtcNow.Date;
            string Iso(DateTime d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "As-of supplier" });
            var bill = NewBill(vendor.Id, ("Rent", 1, 300m, null));
            bill.Date = today.AddDays(-2);
            bill.Status = "Open";
            var approved = await SaveBillOk(bill);
            await BillOk(await Send(HttpMethod.Post, "/api/Purchasing/PayBill",
                new Dto.Purchasing.PayBill { BillId = approved.Id, Amount = 100m, Date = today, PaymentMethod = "Check", ReferenceNo = "CHK-9" }));

            var toVoid = NewBill(vendor.Id, ("Entered twice", 1, 50m, null));
            toVoid.Date = today.AddDays(-2);
            toVoid.Status = "Open";
            var second = await SaveBillOk(toVoid);
            await BillOk(await Send(HttpMethod.Post, $"/api/Purchasing/VoidBill?id={second.Id}&date={IsoDate(DateTime.UtcNow)}&reason=correction"));

            Assert.Equal(0m, (await CallTool("get_payables_aging", new { asOfDate = Iso(today.AddDays(-3)) })).GetProperty("data").GetProperty("total").GetDecimal());

            // Yesterday both bills were owed in full: the payment and the void are dated today.
            var dayBefore = (await CallTool("get_payables_aging", new { asOfDate = Iso(today.AddDays(-1)) })).GetProperty("data");
            Assert.Equal(350m, dayBefore.GetProperty("total").GetDecimal());
            Assert.Equal(2, dayBefore.GetProperty("documentCount").GetInt32());

            var now = (await CallTool("get_payables_aging", new { asOfDate = Iso(today), vendorId = vendor.Id })).GetProperty("data");
            Assert.Equal(200m, now.GetProperty("total").GetDecimal());
            Assert.Equal(200m, (await CallTool("get_payables_aging", new { })).GetProperty("data").GetProperty("total").GetDecimal());

            foreach (var (date, expected) in new[] { (today.AddDays(-1), 350m), (today, 200m) })
            {
                var sheet = await CallTool("get_balance_sheet", new { asOfDate = Iso(date) });
                var payable = sheet.GetProperty("items").EnumerateArray()
                    .Where(i => i.GetProperty("accountCode").GetString() == "20110")
                    .Sum(i => Math.Abs(i.GetProperty("amount").GetDecimal()));
                Assert.Equal(expected, payable);
            }
        }

        [Fact]
        public async Task AssistantTools_BooksChecks_PassOnCleanBooksAndFlagPlantedProblems()
        {
            var today = DateTime.UtcNow.Date;
            string Iso(DateTime d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var period = new { fromDate = Iso(today.AddDays(-10)), toDate = Iso(today) };
            async Task<Dictionary<string, JsonElement>> Checks()
            {
                var result = await CallTool("get_books_checks", period);
                Assert.Equal("Success", result.GetProperty("status").GetString());
                return result.GetProperty("data").GetProperty("checks").EnumerateArray().ToDictionary(c => c.GetProperty("id").GetString());
            }
            string Status(Dictionary<string, JsonElement> checks, string id) => checks[id].GetProperty("status").GetString();

            // Clean books: an invoice and a bill, both posted.
            var invoice = await NewInvoice();
            invoice.Status = "Open";
            Assert.True((await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice)).IsSuccessStatusCode);
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Checks supplier" });
            var bill = NewBill(vendor.Id, ("Rent", 1, 300m, null));
            bill.VendorInvoiceNo = "RENT-1";
            bill.Status = "Open";
            await SaveBillOk(bill);

            var clean = await Checks();
            foreach (var id in new[] { "ledger.balanced", "sales.receivablesTieOut", "sales.unposted", "sales.duplicateNumbers",
                         "purchases.payablesTieOut", "purchases.unposted", "purchases.duplicateVendorInvoice", "purchases.sameDayAmount",
                         "journals.periodEnd", "journals.noMemo", "journals.controlAccounts", "journals.reversedAfterPeriodEnd" })
                Assert.True(Status(clean, id) == "Pass", $"{id} was {Status(clean, id)}: {clean[id].GetRawText()}");
            Assert.Equal(0m, clean["sales.receivablesTieOut"].GetProperty("amount").GetDecimal());
            // The owner in this fixture may not view record history, so that check is reported as not checked.
            Assert.Equal("NotChecked", Status(clean, "journals.postedByPreparer"));

            // Planted: a second bill from the same vendor for the same date and amount (the app itself refuses a repeated
            // vendor invoice number), and a manual journal straight to the receivables account.
            var again = NewBill(vendor.Id, ("Rent", 1, 300m, null));
            again.VendorInvoiceNo = "RENT-1A";
            again.Status = "Open";
            await SaveBillOk(again);
            await PostJournal(today, ("10120", 1, 75m), ("40100", 2, 75m));

            var planted = await Checks();
            Assert.Equal("Pass", Status(planted, "purchases.duplicateVendorInvoice"));
            var duplicate = planted["purchases.sameDayAmount"];
            Assert.Equal("Review", duplicate.GetProperty("status").GetString());
            Assert.Equal(1, duplicate.GetProperty("count").GetInt32());
            Assert.Equal(2, duplicate.GetProperty("examples").GetArrayLength());

            var tieOut = planted["sales.receivablesTieOut"];
            Assert.Equal("Finding", tieOut.GetProperty("status").GetString());
            Assert.Equal(75m, tieOut.GetProperty("amount").GetDecimal());
            var control = planted["journals.controlAccounts"];
            Assert.Equal("Review", control.GetProperty("status").GetString());
            Assert.Equal(1, control.GetProperty("count").GetInt32());
            Assert.Equal("Review", Status(planted, "journals.periodEnd"));
            Assert.Equal("Pass", Status(planted, "purchases.payablesTieOut"));
            Assert.Equal("Pass", Status(planted, "ledger.balanced"));

            var bad = await McpRequest(new { jsonrpc = "2.0", id = 5, method = "tools/call",
                @params = new { name = "get_books_checks", arguments = new { fromDate = Iso(today), toDate = Iso(today.AddDays(-1)) } } });
            Assert.True(bad.GetProperty("result").GetProperty("isError").GetBoolean());
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

            // Payments are listed by their own date, with the bill each one settled.
            await BillOk(await Send(HttpMethod.Post, "/api/Purchasing/PayBill",
                new Dto.Purchasing.PayBill { BillId = approved.Id, Amount = 20m, Date = DateTime.UtcNow, PaymentMethod = "Check", ReferenceNo = "CHK-20" }));
            var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var payments = await CallTool("search_bill_payments", new { fromDate = today, toDate = today });
            Assert.Equal(1, payments.GetProperty("totalCount").GetInt32());
            var payment = payments.GetProperty("data").EnumerateArray().Single();
            Assert.Equal(20m, payment.GetProperty("amount").GetDecimal());
            Assert.Equal(approved.Id, payment.GetProperty("billId").GetInt32());
            Assert.Equal("SUP-778", payment.GetProperty("vendorInvoiceNo").GetString());
            var tomorrow = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(0, (await CallTool("search_bill_payments", new { fromDate = tomorrow })).GetProperty("totalCount").GetInt32());
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
    }
}
