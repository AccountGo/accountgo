#nullable disable
using System;
using Core.Domain.Auditing;
using Core.Domain;
using Core.Domain.Sales;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Services.Inventory;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Dto.Banking;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace AccountGo.BackendTests
{
    /// <summary>Books-review reads, contracts, refusals and evidence isolation.</summary>
    public sealed class MultiTenancyPhase3ReviewToolsTests : MultiTenancyPhase3TestBase
    {
        public MultiTenancyPhase3ReviewToolsTests(ITestOutputHelper output) : base(output) { }

        [Theory]
        [InlineData("get_fiscal_years")]
        [InlineData("get_equity_statement")]
        [InlineData("search_receipts")]
        [InlineData("get_members_and_roles")]
        [InlineData("get_stock_movements")]
        public async Task AuditTools_ExistingReads_ReturnWorkspaceData(string tool)
        {
            var result = await CallTool(tool, new { });
            Assert.Equal("Success", result.GetProperty("status").GetString());
            Assert.Equal(_slug, result.GetProperty("organization").GetString());
            Assert.True(result.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null);
        }

        [Theory]
        [InlineData("search_receipts")]
        [InlineData("get_equity_statement")]
        [InlineData("get_stock_movements")]
        [InlineData("get_record_history")]
        [InlineData("get_bank_outstanding_items")]
        [InlineData("get_tax_report")]
        public async Task AuditTools_UnprivilegedMember_IsRefusedWithoutData(string tool)
        {
            var bank = await CreateBankOk(new CreateBankAccountRequest { Name = "Guarded bank" });
            var bankId = PubId<Core.Domain.Financials.Bank>(bank.Id);
            await JoinOther();
            var result = await CallTool(tool, tool == "get_bank_outstanding_items" ? (object)new { bankId } : new { }, _otherId);
            Assert.Equal("AccessDenied", result.GetProperty("status").GetString());
            Assert.False(result.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null);
        }

        [Fact]
        public async Task AuditTools_BankAging_ReturnsBucketsForRequestedDate()
        {
            var bank = await CreateBankOk(new CreateBankAccountRequest { Name = "Audit checking" });
            var result = await CallTool("get_bank_outstanding_items", new { bankId = PubId<Core.Domain.Financials.Bank>(bank.Id), asOfDate = "2026-01-31" });
            Assert.Equal("Success", result.GetProperty("status").GetString());
            var data = result.GetProperty("data");
            Assert.Equal(PubId<Core.Domain.Financials.Bank>(bank.Id), data.GetProperty("bankId").GetString());
            Assert.Equal(4, data.GetProperty("buckets").GetArrayLength());
        }

        [Fact]
        public async Task AuditTools_MembersRead_IsAvailableToWorkspaceMembers()
        {
            await JoinOther();
            var result = await CallTool("get_members_and_roles", new { }, _otherId);
            Assert.Equal("Success", result.GetProperty("status").GetString());
            var members = result.GetProperty("data").GetProperty("members").EnumerateArray().ToList();
            Assert.Equal(2, members.Count);
            var userTokens = members.Select(m => m.GetProperty("userId").GetString()).ToList();
            Assert.All(userTokens, t => Assert.Matches(@"^P-[0-9A-F]{8}([0-9A-F]{4})?$", t));
            Assert.Equal(2, userTokens.Distinct(StringComparer.Ordinal).Count());
            Assert.DoesNotContain(_ownerId, result.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain(_otherId, result.GetRawText(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task AuditTools_TaxWithoutPeriod_ReturnsSettingsPeriodsAndFilings()
        {
            var result = await CallTool("get_tax_report", new { });
            Assert.Equal("Success", result.GetProperty("status").GetString());
            Assert.Equal("taxReport", result.GetProperty("kind").GetString());
            var data = result.GetProperty("data");
            var settings = await Json(await Send(HttpMethod.Get, "/api/tax/settings"));
            Assert.True(JsonElement.DeepEquals(settings, data.GetProperty("settings")));
            Assert.Equal(JsonValueKind.Array, data.GetProperty("periods").ValueKind);
            Assert.Equal(JsonValueKind.Array, data.GetProperty("filings").ValueKind);
        }

        [Fact]
        public async Task AuditTools_TaxWithPeriod_ReturnsReportWithoutAViewSelector()
        {
            var result = await CallTool("get_tax_report", new { from = "2026-01-01", to = "2026-01-31" });
            Assert.Equal("Success", result.GetProperty("status").GetString());
            Assert.Equal("taxReport", result.GetProperty("kind").GetString());
            var data = result.GetProperty("data");
            Assert.True(data.TryGetProperty("documentsTruncated", out _));
            Assert.True(data.TryGetProperty("filings", out _));
            Assert.True(data.TryGetProperty("ledger", out _));
        }

        [Theory]
        [InlineData("get_tax_report", "{\"from\":\"2026-01-01\"}")]
        [InlineData("get_tax_report", "{\"to\":\"2026-01-31\"}")]
        [InlineData("get_equity_statement", "{\"basis\":\"unknown\"}")]
        [InlineData("get_equity_statement", "{\"compareFrom\":\"2026-01-01\"}")]
        [InlineData("get_equity_statement", "{\"from\":\"2026-02-01\",\"to\":\"2026-01-01\"}")]
        [InlineData("get_bank_outstanding_items", "{\"bankId\":\"0\"}")]
        [InlineData("get_bank_outstanding_items", "{\"bankId\":\"1\",\"asOfDate\":\"bad\"}")]
        [InlineData("search_receipts", "{\"receiptId\":\"0\"}")]
        [InlineData("search_receipts", "{\"take\":101}")]
        [InlineData("search_receipts", "{\"fromDate\":\"2026-02-01\",\"toDate\":\"2026-01-01\"}")]
        [InlineData("get_stock_movements", "{\"itemId\":\"0\"}")]
        [InlineData("get_record_history", "{\"entityId\":\"1\"}")]
        [InlineData("get_record_history", "{\"from\":\"bad\"}")]
        [InlineData("get_record_history", "{\"limit\":101}")]
        public async Task AuditTools_InvalidArguments_ReturnStructuredRefusal(string tool, string json)
        {
            var result = await CallTool(tool, JsonSerializer.Deserialize<JsonElement>(json));
            Assert.Equal("InvalidRequest", result.GetProperty("status").GetString());
            Assert.False(result.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null);
        }

        [Fact]
        public async Task AuditTools_DisabledInventory_ReturnsPlanRestricted()
        {
            SetInventoryModuleEnabled(false);
            var result = await CallTool("get_stock_movements", new { });
            Assert.Equal("PlanRestricted", result.GetProperty("status").GetString());
            Assert.Contains(result.GetProperty("requiredModules").EnumerateArray(), m => m.GetString() == "inventory");
            Assert.False(result.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null);
        }

        [Fact]
        public async Task AuditTools_Receipts_FilterBeforePagingAndSortByDate()
        {
            var invoice = await NewInvoice();
            WithDb(db =>
            {
                foreach (var (number, date) in new[] { ("AUDIT-A", "2026-01-31"), ("AUDIT-B", "2026-02-01"), ("OTHER", "2026-01-30") })
                    db.SalesReceiptHeaders.Add(new SalesReceiptHeader
                    {
                        CustomerId = invoice.CustomerId, No = number, Date = DateTime.Parse(date, CultureInfo.InvariantCulture), Amount = 25m
                    });
                db.SaveChanges();
            });
            var result = await CallTool("search_receipts", new { query = "audit", clientId = PubId<Core.Domain.Sales.Customer>(invoice.CustomerId),
                fromDate = "2026-01-31", toDate = "2026-02-01", skip = 1, take = 1 });
            Assert.Equal(2, result.GetProperty("totalCount").GetInt32());
            var row = Assert.Single(result.GetProperty("data").EnumerateArray());
            Assert.Equal("AUDIT-A", row.GetProperty("receiptNo").GetString());
            Assert.Equal(25m, row.GetProperty("amount").GetDecimal());
        }

        [Fact]
        public async Task AuditTools_StockDateFilter_PreservesRunningQuantity()
        {
            var itemId = CreateStockItem("Audit stock");
            using (var scope = _factory.Services.CreateScope())
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
            {
                var db = scope.ServiceProvider.GetRequiredService<Api.Data.ApiDbContext>();
                var item = db.Items.Include(i => i.InventoryControlJournals).Single(i => i.Id == itemId);
                scope.ServiceProvider.GetRequiredService<IStockService>()
                    .Adjust(item, 10, 5m, "OpeningStock", DateTime.UtcNow.Date.AddDays(-2), "opening");
                db.SaveChanges();
            }
            AdjustStock(itemId, -2, null, "Count", "count");
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var result = await CallTool("get_stock_movements", new { itemId = PubId<Core.Domain.Items.Item>(itemId), fromDate = today, toDate = today, take = 1 });
            Assert.Equal(1, result.GetProperty("totalCount").GetInt32());
            Assert.Equal(8m, Assert.Single(result.GetProperty("data").EnumerateArray()).GetProperty("runningQuantity").GetDecimal());
        }

        [Fact]
        public async Task AuditTools_History_PreservesCursorAndRedactsNestedBankNumbers()
        {
            var historyBank = await CreateBankOk(new CreateBankAccountRequest { Name = "History bank" });
            var historyBankId = PubId<Core.Domain.Financials.Bank>(historyBank.Id);
            WithDb(db =>
            {
                var memberId = db.CompanyMembers.Single(m => m.CompanyId == _companyId && m.UserId == _ownerId).Id;
                var roleId = db.SecurityRoles.Single(r => r.Name == "Auditor / Viewer").Id;
                db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole { CompanyMemberId = memberId, SecurityRoleId = roleId });
                for (var i = 0; i < 3; i++)
                    db.RecordChanges.Add(new RecordChange
                    {
                        CompanyId = _companyId, EntityType = "Bank", EntityId = historyBank.Id, Action = "Update",
                        ChangesJson = "{\"accountNumber\":{\"old\":\"123456789012\",\"new\":\"987654321012\"},\"Name\":{\"old\":\"A\",\"new\":\"B\"}}",
                        ActorUserId = _ownerId, OccurredAt = DateTime.UtcNow.AddDays(-120 - i)
                    });
                db.SaveChanges();
            });
            var first = await CallTool("get_record_history", new { entityType = "Bank", entityId = historyBankId, limit = 2 });
            Assert.Equal("Success", first.GetProperty("status").GetString());
            Assert.DoesNotContain("123456789012", first.GetRawText());
            Assert.DoesNotContain("987654321012", first.GetRawText());
            var data = first.GetProperty("data");
            Assert.Equal(2, data.GetProperty("items").GetArrayLength());
            Assert.Equal(JsonValueKind.Null, data.GetProperty("windowDays").ValueKind);
            var second = await CallTool("get_record_history", new { entityType = "Bank", entityId = historyBankId,
                cursor = data.GetProperty("nextCursor").GetInt64(), limit = 2 });
            Assert.NotEmpty(second.GetProperty("data").GetProperty("items").EnumerateArray());
            Assert.Contains("Name", second.GetRawText());
        }

        [Fact]
        public async Task AuditTools_AnotherWorkspaceRecords_AreNotReturned()
        {
            // Seed real foreign IDs, including history with the same entity ID requested below.
            var originalId = _companyId;
            var originalSlug = _slug;
            var created = await Send(HttpMethod.Post, "/api/organizations", new { name = "Other audit books", slug = "other-audit-books" }, scoped: false);
            Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
            var otherCompanyId = AccountGo.BackendTests.PublicIds.PublicIdTestHelper.ResolveCompanyId(_factory.Services, (await Json(created)).GetProperty("id").GetString());
            string foreignBankId = "", foreignItemId = "", foreignReceiptId = ""; var foreignBankKey = 0;
            try
            {
                _companyId = otherCompanyId;
                _slug = "other-audit-books";
                WithDb(db =>
                {
                    var memberId = db.CompanyMembers.Single(m => m.CompanyId == _companyId && m.UserId == _ownerId).Id;
                    foreach (var roleId in db.SecurityRoles.Where(r => r.Name == "Accountant" || r.Name == "Sales Representative").Select(r => r.Id).ToList())
                        db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole { CompanyMemberId = memberId, SecurityRoleId = roleId });
                    db.SaveChanges();
                });
                await JoinOther();
                foreignBankKey = (await CreateBankOk(new CreateBankAccountRequest { Name = "Foreign audit bank" })).Id;
                foreignBankId = PubId<Core.Domain.Financials.Bank>(foreignBankKey);
                foreignItemId = PubId<Core.Domain.Items.Item>(CreateStockItem("Foreign audit stock"));
                var invoice = await NewInvoice();
                WithDb(db =>
                {
                    db.FinancialYears.Add(new Core.Domain.Financials.FinancialYear { FiscalYearCode = "FOREIGN-FY", FiscalYearName = "Foreign fiscal year",
                        StartDate = new DateTime(2001, 1, 1), EndDate = new DateTime(2001, 12, 31) });
                    var foreignReceipt = new SalesReceiptHeader { CustomerId = invoice.CustomerId, No = "FOREIGN-RECEIPT",
                        Date = DateTime.UtcNow.Date, Amount = 123m };
                    db.SalesReceiptHeaders.Add(foreignReceipt);
                    db.RecordChanges.Add(new RecordChange { CompanyId = _companyId, EntityType = "Bank", EntityId = foreignBankKey,
                        Action = "Update", ChangesJson = "{\"Name\":{\"new\":\"Foreign audit bank\"}}", OccurredAt = DateTime.UtcNow });
                    db.SaveChanges();
                    foreignReceiptId = PubId<Core.Domain.Sales.SalesReceiptHeader>(foreignReceipt.Id);
                });
            }
            finally
            {
                _companyId = originalId;
                _slug = originalSlug;
            }
            WithDb(db =>
            {
                var memberId = db.CompanyMembers.Single(m => m.CompanyId == _companyId && m.UserId == _ownerId).Id;
                db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole { CompanyMemberId = memberId, SecurityRoleId = ViewerRoleId() });
                db.SaveChanges();
            });
            Assert.Equal("NotFound", (await CallTool("get_bank_outstanding_items", new { bankId = foreignBankId })).GetProperty("status").GetString());
            Assert.Equal("NotFound", (await CallTool("get_stock_movements", new { itemId = foreignItemId })).GetProperty("status").GetString());
            var years = await CallTool("get_fiscal_years", new { });
            Assert.DoesNotContain("FOREIGN-FY", years.GetRawText());
            Assert.Equal("NotFound", (await CallTool("search_receipts", new { receiptId = foreignReceiptId })).GetProperty("status").GetString());
            var receipts = await CallTool("search_receipts", new { query = "FOREIGN-RECEIPT" });
            Assert.Equal(0, receipts.GetProperty("totalCount").GetInt32());
            var members = await CallTool("get_members_and_roles", new { });
            Assert.DoesNotContain(members.GetProperty("data").GetProperty("members").EnumerateArray(), m => m.GetProperty("userId").GetString() == _otherId);
            var history = await CallTool("get_record_history", new { entityType = "Bank", entityId = foreignBankId });
            Assert.Equal("NotFound", history.GetProperty("status").GetString());
        }

        [Fact]
        public async Task AuditTools_EquityStatement_PreservesBasisComparisonAndPagePayload()
        {
            const string equitySession = "sess-equity-1";
            var arguments = new { from = "2026-01-01", to = "2026-06-30", basis = "Cash",
                compareFrom = "2025-01-01", compareTo = "2025-06-30" };
            var result = await CallTool("get_equity_statement", arguments, sessionId: equitySession);
            Assert.Equal("Success", result.GetProperty("status").GetString());
            var data = result.GetProperty("data");
            Assert.Equal("Cash", data.GetProperty("basis").GetString());
            Assert.Equal(new DateTime(2025, 6, 30), data.GetProperty("comparative").GetProperty("to").GetDateTime());
            var page = await Json(await Send(HttpMethod.Get,
                "/api/Financials/EquityStatement?from=2026-01-01&to=2026-06-30&basis=Cash&compareFrom=2025-01-01&compareTo=2025-06-30"));
            var expected = await DeidentifyPageData("get_equity_statement", "equityStatement", page, equitySession);
            Assert.True(JsonElement.DeepEquals(expected, data));
            Assert.Equal(page.GetProperty("balanceSheetEquity").GetDecimal(), data.GetProperty("balanceSheetEquity").GetDecimal());
            Assert.Equal(page.GetProperty("differenceToBalanceSheet").GetDecimal(), data.GetProperty("differenceToBalanceSheet").GetDecimal());
        }

        [Fact]
        public async Task AuditTools_FiscalYears_MatchThePageWithoutInsertingYears()
        {
            var count = 0;
            WithDb(db => count = db.FinancialYears.Count());
            var result = await CallTool("get_fiscal_years", new { });
            var page = await Json(await Send(HttpMethod.Get, "/api/Administration/financialyears"));
            Assert.True(JsonElement.DeepEquals(page, result.GetProperty("data")));
            WithDb(db => Assert.Equal(count, db.FinancialYears.Count()));
        }

        [Fact]
        public async Task AuditTools_TaxContext_PreservesFilingFields()
        {
            WithDb(db =>
            {
                var year = DateTime.UtcNow.Year - 3;
                foreach (var month in new[] { 1, 2 })
                    db.TaxFilings.Add(new Core.Domain.TaxSystem.TaxFiling
                    {
                        PeriodStart = new DateTime(year, month, 1), PeriodEnd = new DateTime(year, month, DateTime.DaysInMonth(year, month)),
                        Frequency = TaxPeriodFrequency.Monthly, Basis = AccountingBasis.Accrual, Sequence = 1,
                        FiledOn = new DateTime(year, month + 1, 5), Reference = "AUDIT-FILING-" + month,
                        NetTax = 12m, OutputTax = 12m, SnapshotJson = "{}", CreatedBy = _ownerId, CreatedAt = DateTime.UtcNow
                    });
                db.SaveChanges();
            });
            var result = await CallTool("get_tax_report", new { });
            Assert.Equal("Success", result.GetProperty("status").GetString());
            var filings = result.GetProperty("data").GetProperty("filings");
            Assert.Equal(2, filings.GetArrayLength());
            var row = filings[1];
            Assert.Equal("AUDIT-FILING-1", row.GetProperty("reference").GetString());
            Assert.Equal(12m, row.GetProperty("netTax").GetDecimal());
        }

        [Fact]
        public async Task AuditTools_DisabledEquityModules_ReturnPlanRestricted()
        {
            WithDb(db =>
            {
                db.Companies.Single(c => c.Id == _companyId).DisabledModulesJson =
                    "[\"sales\",\"purchases\",\"financials\",\"reports\"]";
                db.SaveChanges();
            });
            var result = await CallTool("get_equity_statement", new { });
            Assert.Equal("PlanRestricted", result.GetProperty("status").GetString());
            Assert.False(result.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null);
        }

        [Fact]
        public async Task AuditTools_ReceiptDetail_MatchesPageAndRefusesMissingOrUnauthorizedIds()
        {
            var invoice = await NewInvoice();
            var receiptId = 0;
            WithDb(db =>
            {
                var receipt = new SalesReceiptHeader { CustomerId = invoice.CustomerId, No = "DETAIL-RECEIPT",
                    Date = DateTime.UtcNow.Date, Amount = 37.25m, Memo = "Receipt detail evidence" };
                db.SalesReceiptHeaders.Add(receipt);
                db.SaveChanges();
                receiptId = receipt.Id;
            });
            const string receiptSession = "sess-receipt-1";
            var result = await CallTool("search_receipts", new { receiptId = PubId<Core.Domain.Sales.SalesReceiptHeader>(receiptId) }, sessionId: receiptSession);
            Assert.Equal("Success", result.GetProperty("status").GetString());
            var data = result.GetProperty("data");
            Assert.Equal(PubId<Core.Domain.Sales.SalesReceiptHeader>(receiptId), data.GetProperty("id").GetString());
            Assert.Equal(37.25m, data.GetProperty("amount").GetDecimal());
            Assert.Equal("DETAIL-RECEIPT", data.GetProperty("receiptNo").GetString());
            Assert.Matches(@"^C-[0-9A-F]{8}([0-9A-F]{4})?$", data.GetProperty("customerName").GetString());
            Assert.Equal(Api.Service.Ai.McpFieldClassificationRegistry.HiddenTextPlaceholder, data.GetProperty("memo").GetString());
            Assert.DoesNotContain("Receipt detail evidence", data.GetRawText(), StringComparison.OrdinalIgnoreCase);
            var page = await Json(await Send(HttpMethod.Get, $"/api/Sales/SalesReceipt?id={PubId<Core.Domain.Sales.SalesReceiptHeader>(receiptId)}"));
            var expected = await DeidentifyPageData("search_receipts", "receipt", page, receiptSession);
            Assert.True(JsonElement.DeepEquals(expected, data));
            Assert.Equal("NotFound", (await CallTool("search_receipts", new { receiptId = "rcpt_ffffffffffffffffffffffffff" })).GetProperty("status").GetString());
            await JoinOther();
            Assert.Equal("AccessDenied", (await CallTool("search_receipts", new { receiptId = PubId<Core.Domain.Sales.SalesReceiptHeader>(receiptId) }, _otherId)).GetProperty("status").GetString());
        }

        [Fact]
        public async Task AuditTools_MembersAndReceipts_DoNotExposeFullBankNumbers()
        {
            var bank = await CreateBankOk(new CreateBankAccountRequest
            {
                Name = "Review receipt bank", AccountNumber = "123456789012", RoutingNumber = "021000021",
                Iban = "GB33BUKB20201555555555"
            });
            var invoice = await NewInvoice();
            var receiptId = 0;
            WithDb(db =>
            {
                var receipt = new SalesReceiptHeader { CustomerId = invoice.CustomerId, No = "BANK-RECEIPT",
                    AccountToDebitId = bank.AccountId, Date = DateTime.UtcNow.Date, Amount = 42m };
                db.SalesReceiptHeaders.Add(receipt);
                db.SaveChanges();
                receiptId = receipt.Id;
            });
            var members = await CallTool("get_members_and_roles", new { });
            Assert.Equal("Success", members.GetProperty("status").GetString());
            var memberRows = members.GetProperty("data").GetProperty("members").EnumerateArray().ToList();
            Assert.NotEmpty(memberRows);
            Assert.All(memberRows, m => Assert.Matches(@"^P-[0-9A-F]{8}([0-9A-F]{4})?$", m.GetProperty("userId").GetString()));
            Assert.DoesNotContain(_ownerId, members.GetRawText(), StringComparison.Ordinal);
            var receipts = await CallTool("search_receipts", new { query = "BANK-RECEIPT" });
            Assert.Single(receipts.GetProperty("data").EnumerateArray());
            var detail = await CallTool("search_receipts", new { receiptId = PubId<Core.Domain.Sales.SalesReceiptHeader>(receiptId) });
            Assert.Equal(42m, detail.GetProperty("data").GetProperty("amount").GetDecimal());
            foreach (var result in new[] { members, receipts, detail })
            {
                Assert.DoesNotContain("123456789012", result.GetRawText());
                Assert.DoesNotContain("021000021", result.GetRawText());
                Assert.DoesNotContain("GB33BUKB20201555555555", result.GetRawText());
            }
        }

    }
}
