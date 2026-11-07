#nullable disable
using System;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using AccountGo.BackendTests.Observability;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Dto.Banking;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AccountGo.BackendTests;

public partial class MultiTenancyPhase3Tests
{
    [Fact]
    public async Task PerformanceBooks_PostedDocumentsAndReconciliationAreCreatedThroughEndpoints()
    {
        static DateTime Date(string value) => DateTime.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        async Task<T> Post<T>(string path, object value)
        {
            using var response = await Send(HttpMethod.Post, path, value);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<T>();
        }
        var firstDate = Date("2026-02-01");
        var secondDate = Date("2027-02-01");
        var first = await CreateBankOk(new CreateBankAccountRequest { Name = "Performance operating", Type = 1, OpeningBalance = 1000m, OpeningBalanceDate = firstDate });
        var second = await CreateBankOk(new CreateBankAccountRequest { Name = "Performance reserve", Type = 1 });
        var client = await Post<Dto.Sales.Client>("/api/Sales/SaveClient", new Dto.Sales.Client { Name = "Performance client" });
        var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Performance vendor" });
        var item = await SaveItem(new Dto.Inventory.CatalogItem { Name = "Performance service", Price = 100m, SalesAccountId = AccountId("40100") });
        int unitId = 0;
        WithDb(db => unitId = db.Items.Single(i => i.Id == item.Id).SmallestMeasurementId.Value);
        async Task AddBooks(DateTime date, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var invoice = new Dto.Sales.SalesInvoice
                {
                    CustomerId = client.Id, InvoiceDate = date, Status = "Open",
                    SalesInvoiceLines = new[] { new Dto.Sales.SalesInvoiceLine { ItemId = item.Id, MeasurementId = unitId, Quantity = 1, Amount = 100m } }
                };
                var savedInvoice = await Post<Dto.Sales.SalesInvoice>("/api/Sales/SaveSalesInvoice", invoice);
                await Post<System.Text.Json.JsonElement>("/api/Sales/RecordPayment", new Dto.Sales.RecordInvoicePaymentDto
                { InvoiceId = savedInvoice.Id, Amount = 100m, PaymentDate = date, BankAccountId = first.Id });
                var bill = NewBill(vendor.Id, ("Performance expense", 1m, 40m, AccountId("50200")));
                bill.Date = date; bill.Status = "Open";
                var savedBill = await SaveBillOk(bill);
                await Post<System.Text.Json.JsonElement>("/api/Purchasing/PayBill", new Dto.Purchasing.PayBill
                { BillId = savedBill.Id, Amount = 40m, Date = date, BankAccountId = first.Id });
                await Post<BankTransactionDto>("/api/Banking/Transactions", new CreateBankTransactionRequest
                { Kind = 1, BankId = first.Id, ContraAccountId = AccountId("40100"), Date = date, Amount = 10m });
                await Post<BankTransactionDto>("/api/Banking/Transactions", new CreateBankTransactionRequest
                { Kind = 3, BankId = first.Id, ToBankId = second.Id, Date = date, Amount = 5m });
                var journal = new Dto.Financial.JournalEntry { JournalDate = date, VoucherType = 6, Memo = "Performance journal" };
                journal.JournalEntryLines.Add(new Dto.Financial.JournalEntryLine { AccountId = AccountId("10111"), DrCr = 1, Amount = 25m });
                journal.JournalEntryLines.Add(new Dto.Financial.JournalEntryLine { AccountId = AccountId("40100"), DrCr = 2, Amount = 25m });
                journal.Id = (await Post<Dto.Financial.JournalEntry>("/api/Financials/SaveJournalEntry", journal)).Id;
                var approval = await ApproveJournalPostAsync(journal.Id);
                using var posted = await Send(HttpMethod.Post, "/api/Financials/PostJournalEntry", journal, extraHeaders: approval);
                Assert.True(posted.IsSuccessStatusCode, await posted.Content.ReadAsStringAsync());
            }
        }
        await AddBooks(firstDate, 1);
        var draft = await Post<BankReconciliationDto>("/api/Banking/Reconciliations", new StartBankReconciliationRequest
        { BankId = first.Id, StatementEndDate = Date("2026-12-31"), StatementEndingBalance = 1090m });
        using (var request = new HttpRequestMessage(HttpMethod.Put, $"/api/Banking/Reconciliations/{draft.Id}/Items"))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _ownerToken);
            request.Headers.Add("X-AccountGo-Org", _slug);
            request.Content = JsonContent.Create(new UpdateBankReconciliationItemsRequest
            { Add = draft.Deposits.Concat(draft.Payments).Where(x => !x.Ticked).Select(x => x.LineId).ToArray(), Version = draft.Version });
            using var response = await _client.SendAsync(request);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            draft = await response.Content.ReadFromJsonAsync<BankReconciliationDto>();
        }
        Assert.Equal(0m, draft.Difference);
        var finished = await Post<BankReconciliationDto>($"/api/Banking/Reconciliations/{draft.Id}/Finish", new FinishBankReconciliationRequest { Version = draft.Version });
        Assert.NotNull(finished.FinishedAt);
        await AddBooks(secondDate, 1);
        var query = "?from=2026-01-01T00:00:00&to=2027-12-31T23:59:59";
        var golden = new JsonObject();
        foreach (var report in new[] { "trialbalance", "BalanceSheet", "IncomeStatement", "CashFlow", "GeneralLedger" })
        {
            using var response = await Send(HttpMethod.Get, "/api/Financials/" + report + query);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            golden[report] = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        }
        using (var response = await Send(HttpMethod.Get, "/api/Dashboard/Summary"))
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var kpis = JsonNode.Parse(await response.Content.ReadAsStringAsync())["kpis"];
            golden["dashboard"] = new JsonObject
            {
                ["outstandingReceivables"] = kpis["outstandingReceivables"]?.DeepClone(),
                ["totalPayables"] = kpis["totalPayables"]?.DeepClone(),
                ["moneyInBank"] = kpis["moneyInBank"]?.DeepClone()
            };
        }
        // Ledger-derived dashboard figures over a fixed range, so the snapshot does not depend on today's date.
        using (var response = await Send(HttpMethod.Get, "/api/Dashboard/Summary?from=2026-01-01&to=2027-12-31"))
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var summary = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            var ledgerKpis = new JsonObject();
            foreach (var key in new[] { "totalAssets", "totalSalesYtd", "earnedIncomeYtd", "moneyFromFreelanceWorkYtd",
                         "drawingsYtd", "totalExpensesYtd", "netProfitYtd", "cashFlowYtd" })
                ledgerKpis[key] = summary["kpis"][key]?.DeepClone();
            golden["dashboardLedger"] = new JsonObject
            {
                ["kpis"] = ledgerKpis,
                ["monthlySales"] = summary["monthlySales"]?.DeepClone(),
                ["monthlyExpenses"] = summary["monthlyExpenses"]?.DeepClone()
            };
        }
        using (var response = await Send(HttpMethod.Get, $"/api/Banking/Accounts/{first.Id}/Register?from=2026-01-01&to=2027-12-31&pageSize=100"))
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            golden["bankRegister"] = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        }
        using (var response = await Send(HttpMethod.Get, "/api/Banking/Accounts"))
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var banks = JsonNode.Parse(await response.Content.ReadAsStringAsync())["accounts"].AsArray();
            golden["bankBalances"] = new JsonArray(banks.Select(bank => (JsonNode)new JsonObject
            {
                ["name"] = bank["name"]?.DeepClone(), ["bookBalance"] = bank["bookBalance"]?.DeepClone()
            }).ToArray());
        }
        golden["reconciliation"] = new JsonObject
        {
            ["beginningBalance"] = finished.BeginningBalance, ["endingBalance"] = finished.StatementEndingBalance,
            ["clearedDeposits"] = finished.ClearedDeposits, ["clearedPayments"] = finished.ClearedPayments,
            ["clearedBalance"] = finished.ClearedBalance, ["difference"] = finished.Difference
        };
        var normalized = NormalizeGolden(golden);
        _output.WriteLine("GOLDEN " + normalized.ToJsonString());
        var snapshotPath = Path.Combine(Path.GetDirectoryName(PerformanceFixtureSource())!, "Observability", "performance-books-golden.json");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(snapshotPath)), normalized), "Golden report output changed");
        using var collector = new RequestQueryBudgetTests.RequestCommandCollector();
        WithDb(db => collector.Database = RequestQueryBudgetTests.RequestCommandCollector.ConnectionKey(db.Database.GetDbConnection()));
        var counts = new Dictionary<string, Dictionary<string, int>>();
        async Task Measure(string scale)
        {
            foreach (var (path, _) in RequestQueryBudgetTests.Budgets)
            {
                var commands = new List<string>();
                collector.Current = commands;
                try { using var result = await Send(HttpMethod.Get, path); await result.Content.ReadAsStringAsync(); }
                finally { collector.Current = null; }
                Assert.NotEmpty(commands);
                _output.WriteLine($"VOLUME {scale} {path} {commands.Count}");
                if (!counts.TryGetValue(path, out var byScale)) counts[path] = byScale = new Dictionary<string, int>();
                byScale[scale] = commands.Count;
            }
        }
        await Measure("1x");
        for (var i = 1; i <= 8; i++) await AddBooks(secondDate.AddDays(i), 1);
        await Measure("5x");
        // No endpoint may run more queries because the books hold more documents.
        var growing = counts.Where(c => c.Value["5x"] > c.Value["1x"]).Select(c => $"{c.Key}: {c.Value["1x"]} -> {c.Value["5x"]}").ToList();
        Assert.True(growing.Count == 0, "Query count grows with volume: " + string.Join(", ", growing));
    }
    private static string PerformanceFixtureSource([CallerFilePath] string path = "") => path;

    private static JsonNode NormalizeGolden(JsonNode node)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "id", "accountId", "lineId", "headerId", "bankId", "reconciliationId", "sourceDocumentId", "sourcePath" };
        if (node is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var pair in obj)
                if (!ignored.Contains(pair.Key)) result[pair.Key] = pair.Value == null ? null : NormalizeGolden(pair.Value);
            return result;
        }
        if (node is JsonArray array)
        {
            var normalized = array.Select(value => value == null ? null : NormalizeGolden(value))
                .OrderBy(value => value?.ToJsonString(), StringComparer.Ordinal).ToArray();
            var result = new JsonArray();
            foreach (var value in normalized) result.Add(value);
            return result;
        }
        return node.DeepClone();
    }

}
