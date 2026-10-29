#nullable disable
using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Security;
using Core.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests
{
    public partial class MultiTenancyPhase3Tests
    {
        [Fact]
        public async Task GeneralLedger_FiltersHeaderDatesAndAccounts_RetainsBalancedLinesAndAuditMetadata()
        {
            var start = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddMonths(1).AddTicks(-1);
            int firstId = 0, lastId = 0;
            WithDb(db =>
            {
                var cash = db.Accounts.Single(a => a.AccountCode == "10111");
                var revenue = db.Accounts.Single(a => a.AccountCode == "40100");
                var first = ReportEntry(start, cash, revenue, 123.45m);
                first.DocumentType = DocumentTypes.SalesInvoice;
                first.SourceDocumentId = 42;
                first.Description = "Invoice INV-42";
                var last = ReportEntry(end, revenue, cash, 123.45m);
                last.DocumentType = DocumentTypes.PurchaseInvoice;
                last.SourceDocumentId = 43;
                db.GeneralLedgerHeaders.AddRange(ReportEntry(start.AddTicks(-1), cash, revenue, 1m), first,
                    last, ReportEntry(end.AddTicks(1), cash, revenue, 900m));
                db.SaveChanges();
                last.ReversedGeneralLedgerHeaderId = first.Id;
                db.SaveChanges();
                firstId = first.Id;
                lastId = last.Id;
            });
            var query = "?from=" + start.ToString("O", CultureInfo.InvariantCulture)
                + "&to=" + end.ToString("O", CultureInfo.InvariantCulture) + "&accountCode=10111";
            var response = await Send(HttpMethod.Get, "/api/Financials/GeneralLedger" + query);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var page = await Json(response);
            var rows = page.GetProperty("items").EnumerateArray().ToList();
            Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
            Assert.Equal(new[] { lastId, firstId }, rows.Select(row => row.GetProperty("id").GetInt32()));
            Assert.All(rows, row =>
            {
                Assert.Equal(123.45m, row.GetProperty("totalDebit").GetDecimal());
                Assert.Equal(row.GetProperty("totalDebit").GetDecimal(), row.GetProperty("totalCredit").GetDecimal());
                var lines = row.GetProperty("lines").EnumerateArray().ToList();
                Assert.Equal(2, lines.Count);
                Assert.Contains(lines, line => line.GetProperty("accountCode").GetString() == "40100");
                Assert.Equal(123.45m, lines.Sum(line => line.GetProperty("debit").GetDecimal()));
                Assert.Equal(123.45m, lines.Sum(line => line.GetProperty("credit").GetDecimal()));
            });
            Assert.Equal("SalesInvoice", rows[1].GetProperty("documentType").GetString());
            Assert.Equal("Sales Invoice", rows[1].GetProperty("documentTypeLabel").GetString());
            Assert.Equal("Invoice INV-42", rows[1].GetProperty("description").GetString());
            Assert.Equal("/sales/42", rows[1].GetProperty("sourcePath").GetString());
            Assert.Equal("/purchases/43", rows[0].GetProperty("sourcePath").GetString());
            Assert.Equal(firstId, rows[0].GetProperty("reversedGeneralLedgerHeaderId").GetInt32());
            var byType = await Json(await Send(HttpMethod.Get, "/api/Financials/GeneralLedger" + query + "&documentType=SalesInvoice"));
            Assert.Equal(firstId, Assert.Single(byType.GetProperty("items").EnumerateArray()).GetProperty("id").GetInt32());
            var byId = await Json(await Send(HttpMethod.Get, "/api/Financials/GeneralLedger?transactionNo=" + lastId));
            Assert.Equal(lastId, Assert.Single(byId.GetProperty("items").EnumerateArray()).GetProperty("id").GetInt32());
            Assert.Empty((await Json(await Send(HttpMethod.Get, "/api/Financials/GeneralLedger?accountCode=missing"))).GetProperty("items").EnumerateArray());
        }

        [Theory]
        [InlineData("?from=2026-05-01&to=2026-04-01")]
        [InlineData("?from=invalid")]
        [InlineData("?to=invalid")]
        [InlineData("?transactionNo=invalid")]
        [InlineData("?documentType=invalid")]
        [InlineData("?documentType=99")]
        [InlineData("?skip=-1")]
        [InlineData("?take=0")]
        [InlineData("?take=501")]
        public async Task GeneralLedger_InvalidFilters_ReturnBadRequest(string query)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, "/api/Financials/GeneralLedger" + query)).StatusCode);
        }

        [Fact]
        public async Task GeneralLedger_InvoiceVoidAndBillPayment_ResolveSourcesAndRetainBalancedAuditTrail()
        {
            var invoice = await NewInvoice();
            invoice.SalesInvoiceLines[0].ItemId = null;
            invoice.SalesInvoiceLines[0].ItemDescription = "Ledger advisory hours";
            invoice.Status = "Open";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var invoiceId = (await Json(saved)).GetProperty("id").GetInt32();
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, $"/api/Sales/VoidSalesInvoice?id={invoiceId}&date={IsoDate(DateTime.UtcNow)}")).StatusCode);
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Ledger supplier" });
            var bill = NewBill(vendor.Id, ("Ledger expense", 1, 100m, null));
            bill.Status = "Open";
            var approved = await SaveBillOk(bill);
            var paid = await BillOk(await Send(HttpMethod.Post, "/api/Purchasing/PayBill",
                new Dto.Purchasing.PayBill { BillId = approved.Id, Amount = 100m, Date = DateTime.UtcNow }));
            var response = await Send(HttpMethod.Get, "/api/Financials/GeneralLedger");
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var rows = (await Json(response)).GetProperty("items").EnumerateArray().ToList();
            var sales = rows.Where(r => r.GetProperty("documentType").GetString() == "SalesInvoice").ToList();
            Assert.Equal(2, sales.Count);
            Assert.All(sales, r => Assert.Equal("/sales/" + invoiceId, r.GetProperty("sourcePath").GetString()));
            var original = sales.Single(r => r.GetProperty("reversedGeneralLedgerHeaderId").ValueKind == System.Text.Json.JsonValueKind.Null);
            var reversal = sales.Single(r => r.GetProperty("reversedGeneralLedgerHeaderId").ValueKind != System.Text.Json.JsonValueKind.Null);
            Assert.Equal(original.GetProperty("id").GetInt32(), reversal.GetProperty("reversedGeneralLedgerHeaderId").GetInt32());
            var netByAccount = sales.SelectMany(r => r.GetProperty("lines").EnumerateArray())
                .GroupBy(l => l.GetProperty("accountId").GetInt32());
            Assert.All(netByAccount, g => Assert.Equal(0m, g.Sum(l => l.GetProperty("debit").GetDecimal() - l.GetProperty("credit").GetDecimal())));
            var purchases = rows.Where(r => r.GetProperty("documentType").GetString().StartsWith("PurchaseInvoice", StringComparison.Ordinal)).ToList();
            Assert.Equal(2, purchases.Count);
            Assert.All(purchases, r => Assert.Equal("/purchases/" + approved.Id, r.GetProperty("sourcePath").GetString()));
            Assert.Contains(purchases, r => r.GetProperty("documentType").GetString() == "PurchaseInvoicePayment"
                && r.GetProperty("sourceDocumentId").GetInt32() == approved.Id);
            Assert.Single(paid.Payments);
            Assert.All(rows, r => Assert.Equal(r.GetProperty("totalDebit").GetDecimal(), r.GetProperty("totalCredit").GetDecimal()));
        }

        [Fact]
        public async Task GeneralLedger_OrdersSameDateById_AndSurfacesUnbalancedLegacyEntries()
        {
            int firstId = 0, secondId = 0;
            WithDb(db =>
            {
                var cash = db.Accounts.Single(a => a.AccountCode == "10111");
                var revenue = db.Accounts.Single(a => a.AccountCode == "40100");
                var date = new DateTime(2026, 4, 1);
                var first = ReportEntry(date, cash, revenue, 100m);
                var second = ReportEntry(date, cash, revenue, 200m);
                second.GeneralLedgerLines.Last().Amount = 150m;
                db.GeneralLedgerHeaders.AddRange(first, second);
                db.SaveChanges();
                firstId = first.Id; secondId = second.Id;
            });
            var rows = (await Json(await Send(HttpMethod.Get, "/api/Financials/GeneralLedger"))).GetProperty("items").EnumerateArray().ToList();
            Assert.Equal(new[] { Math.Max(firstId, secondId), Math.Min(firstId, secondId) }, rows.Select(r => r.GetProperty("id").GetInt32()));
            var unbalanced = rows.Single(r => r.GetProperty("id").GetInt32() == secondId);
            Assert.NotEqual(unbalanced.GetProperty("totalDebit").GetDecimal(), unbalanced.GetProperty("totalCredit").GetDecimal());
        }

        [Fact]
        public async Task GeneralLedger_AndJournalLifecycle_AreTenantScoped()
        {
            var created = await Send(HttpMethod.Post, "/api/organizations",
                new { name = "Other books", slug = "other-books" }, token: _otherToken, scoped: false);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var otherCompany = int.Parse((await Json(created)).GetProperty("id").GetString());
            int foreignGl = 0, foreignJournal = 0, foreignAccount = 0;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(otherCompany, "other-books", CompanyMemberRole.Owner))
                {
                    var accounts = db.Accounts.Where(a => !a.ChildAccounts.Any()).Take(2).ToList();
                    var header = new GeneralLedgerHeader
                    {
                        Date = DateTime.UtcNow, Description = "Foreign secret", DocumentType = DocumentTypes.JournalEntry,
                        GeneralLedgerLines =
                        {
                            new GeneralLedgerLine { AccountId = accounts[0].Id, Amount = 50m, DrCr = DrOrCrSide.Dr },
                            new GeneralLedgerLine { AccountId = accounts[1].Id, Amount = 50m, DrCr = DrOrCrSide.Cr }
                        }
                    };
                    var journal = new JournalEntryHeader { Date = DateTime.UtcNow, Memo = "Foreign draft", Posted = false };
                    db.GeneralLedgerHeaders.Add(header);
                    db.JournalEntryHeaders.Add(journal);
                    db.SaveChanges();
                    foreignGl = header.Id; foreignJournal = journal.Id; foreignAccount = accounts[0].Id;
                }
            }
            foreach (var query in new[] { "", "?transactionNo=" + foreignGl, "?accountCode=10111" })
                Assert.Empty((await Json(await Send(HttpMethod.Get, "/api/Financials/GeneralLedger" + query))).GetProperty("items").EnumerateArray());
            Assert.Empty((await Json(await Send(HttpMethod.Get, "/api/Financials/JournalEntries"))).EnumerateArray());
            Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, "/api/Financials/JournalEntry?id=" + foreignJournal)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Post, "/api/Financials/PostJournalEntry", new { id = foreignJournal })).StatusCode);
            var own = LedgerDraft();
            own.Id = foreignJournal;
            Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Post, "/api/Financials/SaveJournalEntry", own)).StatusCode);
            own.Id = 0;
            own.JournalEntryLines[0].AccountId = foreignAccount;
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/Financials/SaveJournalEntry", own)).StatusCode);
        }

        [Fact]
        public async Task GeneralLedger_JournalPermissions_AreIndependentOfAccountView()
        {
            await JoinOther();
            var roleId = 0;
            WithDb(db =>
            {
                var role = new SecurityRole { Name = "Ledger test", DisplayName = "Ledger test" };
                role.Permissions.Add(new SecurityRolePermission
                {
                    SecurityPermissionId = db.SecurityPermissions.Single(p => p.Name == SystemPermissions.FinancialsAccountsView).Id
                });
                db.SecurityRoles.Add(role); db.SaveChanges(); roleId = role.Id;
            });
            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Patch, Current + "/members/" + MemberId(_otherId),
                new { securityRoleIds = new[] { roleId } })).StatusCode);
            var denied = await Send(HttpMethod.Get, "/api/Financials/GeneralLedger", token: _otherToken);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Contains("financials.journal.view", await denied.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/Financials/Accounts", token: _otherToken)).StatusCode);
            foreach (var path in new[] { "JournalEntries", "JournalEntry?id=1" })
                Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/Financials/" + path, token: _otherToken)).StatusCode);

            WithDb(db =>
            {
                foreach (var permission in new[] { SystemPermissions.FinancialsJournalView, SystemPermissions.FinancialsJournalCreate })
                    db.SecurityRolePermissions.Add(new SecurityRolePermission { SecurityRoleId = roleId,
                        SecurityPermissionId = db.SecurityPermissions.Single(p => p.Name == permission).Id });
                db.SaveChanges();
            });
            var draft = LedgerDraft();
            var save = await Send(HttpMethod.Post, "/api/Financials/SaveJournalEntry", draft, _otherToken);
            Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
            draft.Id = (await Json(save)).GetProperty("id").GetInt32();
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/Financials/GeneralLedger", token: _otherToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "/api/Financials/PostJournalEntry", draft, _otherToken)).StatusCode);

            WithDb(db =>
            {
                db.SecurityRolePermissions.RemoveRange(db.SecurityRolePermissions.Where(p => p.SecurityRoleId == roleId
                    && p.SecurityPermission.Name == SystemPermissions.FinancialsJournalCreate));
                db.SecurityRolePermissions.Add(new SecurityRolePermission { SecurityRoleId = roleId,
                    SecurityPermissionId = db.SecurityPermissions.Single(p => p.Name == SystemPermissions.FinancialsJournalPost).Id });
                db.SaveChanges();
            });
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "/api/Financials/SaveJournalEntry", draft, _otherToken)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, "/api/Financials/PostJournalEntry", draft, _otherToken)).StatusCode);
        }

        [Fact]
        public async Task GeneralLedger_ManualDraftEditAndPost_PreservesDateAndRejectsPostedEditsAndDuplicates()
        {
            var draft = LedgerDraft();
            var save = await Send(HttpMethod.Post, "/api/Financials/SaveJournalEntry", draft);
            Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
            draft.Id = (await Json(save)).GetProperty("id").GetInt32();
            Assert.Empty((await Json(await Send(HttpMethod.Get, "/api/Financials/GeneralLedger"))).GetProperty("items").EnumerateArray());
            draft.Memo = "Updated draft";
            draft.JournalEntryLines = (await (await Send(HttpMethod.Get, "/api/Financials/JournalEntry?id=" + draft.Id))
                .Content.ReadFromJsonAsync<Dto.Financial.JournalEntry>()).JournalEntryLines;
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, "/api/Financials/SaveJournalEntry", draft)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, "/api/Financials/PostJournalEntry", draft)).StatusCode);
            var posted = Assert.Single((await Json(await Send(HttpMethod.Get, "/api/Financials/GeneralLedger"))).GetProperty("items").EnumerateArray());
            Assert.Equal(draft.JournalDate, posted.GetProperty("date").GetDateTime());
            Assert.Equal("Updated draft", posted.GetProperty("description").GetString());
            Assert.Equal("/financials/journals?id=" + draft.Id, posted.GetProperty("sourcePath").GetString());
            Assert.Equal(draft.Id, posted.GetProperty("sourceDocumentId").GetInt32());
            Assert.Equal(25m, posted.GetProperty("totalDebit").GetDecimal());
            Assert.Equal(25m, posted.GetProperty("totalCredit").GetDecimal());
            draft.Memo = "Must not replace posted memo";
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/Financials/SaveJournalEntry", draft)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/Financials/PostJournalEntry", draft)).StatusCode);
            var after = Assert.Single((await Json(await Send(HttpMethod.Get, "/api/Financials/GeneralLedger"))).GetProperty("items").EnumerateArray());
            Assert.Equal(posted.GetRawText(), after.GetRawText());
            var voucher = await Json(await Send(HttpMethod.Get, "/api/Financials/JournalEntry?id=" + draft.Id));
            Assert.True(voucher.GetProperty("posted").GetBoolean());
            Assert.Equal("Updated draft", voucher.GetProperty("memo").GetString());
        }

        [Fact]
        public async Task GeneralLedger_UnbalancedDraftCannotPost_AndClosedPeriodRejectsPosting()
        {
            var draft = LedgerDraft();
            draft.JournalEntryLines[1].Amount = 20m;
            var saved = await Send(HttpMethod.Post, "/api/Financials/SaveJournalEntry", draft);
            draft.Id = (await Json(saved)).GetProperty("id").GetInt32();
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/Financials/PostJournalEntry", draft)).StatusCode);
            WithDb(db => Assert.Empty(db.GeneralLedgerHeaders));
            draft.JournalEntryLines[1].Amount = 25m;
            draft.JournalDate = new DateTime(1900, 1, 1);
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, "/api/Financials/SaveJournalEntry", draft)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/Financials/PostJournalEntry", draft)).StatusCode);
            WithDb(db =>
            {
                Assert.Empty(db.GeneralLedgerHeaders);
                Assert.False(db.JournalEntryHeaders.Single(j => j.Id == draft.Id).Posted);
            });
        }


        [Fact]
        public async Task GeneralLedger_PagesResults_AndDateOnlyToIncludesEndOfDay()
        {
            int firstId = 0, secondId = 0, thirdId = 0;
            WithDb(db =>
            {
                var cash = db.Accounts.Single(a => a.AccountCode == "10111");
                var revenue = db.Accounts.Single(a => a.AccountCode == "40100");
                var day = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Unspecified);
                var first = ReportEntry(day, cash, revenue, 10m);
                var second = ReportEntry(day.AddHours(12), cash, revenue, 20m);
                var third = ReportEntry(day.AddDays(1).AddTicks(-1), cash, revenue, 30m);
                db.GeneralLedgerHeaders.AddRange(first, second, third);
                db.SaveChanges();
                firstId = first.Id; secondId = second.Id; thirdId = third.Id;
            });
            var dateOnly = await Json(await Send(HttpMethod.Get, "/api/Financials/GeneralLedger?from=2026-04-15&to=2026-04-15"));
            Assert.Equal(3, dateOnly.GetProperty("totalCount").GetInt32());
            Assert.Equal(3, dateOnly.GetProperty("items").GetArrayLength());
            var page1 = await Json(await Send(HttpMethod.Get, "/api/Financials/GeneralLedger?from=2026-04-15&to=2026-04-15&skip=0&take=2"));
            Assert.Equal(3, page1.GetProperty("totalCount").GetInt32());
            Assert.Equal(2, page1.GetProperty("take").GetInt32());
            Assert.Equal(new[] { thirdId, secondId }, page1.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("id").GetInt32()).ToArray());
            var page2 = await Json(await Send(HttpMethod.Get, "/api/Financials/GeneralLedger?from=2026-04-15&to=2026-04-15&skip=2&take=2"));
            Assert.Equal(firstId, Assert.Single(page2.GetProperty("items").EnumerateArray()).GetProperty("id").GetInt32());
        }

        [Fact]
        public async Task GeneralLedger_ManualJournal_AllowsSplitLinesToSameAccount()
        {
            var cash = AccountId("10111");
            var draft = new Dto.Financial.JournalEntry
            {
                JournalDate = DateTime.UtcNow.Date.AddDays(-1), VoucherType = 6, Memo = "Split expense",
                JournalEntryLines =
                {
                    new Dto.Financial.JournalEntryLine { AccountId = cash, DrCr = 1, Amount = 10m, Memo = "Part A" },
                    new Dto.Financial.JournalEntryLine { AccountId = cash, DrCr = 1, Amount = 15m, Memo = "Part B" },
                    new Dto.Financial.JournalEntryLine { AccountId = AccountId("40100"), DrCr = 2, Amount = 25m, Memo = "Offset" }
                }
            };
            var save = await Send(HttpMethod.Post, "/api/Financials/SaveJournalEntry", draft);
            Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
            draft.Id = (await Json(save)).GetProperty("id").GetInt32();
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, "/api/Financials/PostJournalEntry", draft)).StatusCode);
            var posted = Assert.Single((await Json(await Send(HttpMethod.Get, "/api/Financials/GeneralLedger"))).GetProperty("items").EnumerateArray());
            Assert.Equal(3, posted.GetProperty("lines").GetArrayLength());
            Assert.Equal(2, posted.GetProperty("lines").EnumerateArray().Count(l => l.GetProperty("accountId").GetInt32() == cash));
            Assert.Equal(25m, posted.GetProperty("totalDebit").GetDecimal());
            Assert.Equal(25m, posted.GetProperty("totalCredit").GetDecimal());
        }

        private Dto.Financial.JournalEntry LedgerDraft()
        {
            return new Dto.Financial.JournalEntry
            {
                JournalDate = DateTime.UtcNow.Date.AddDays(-1), VoucherType = 6, Memo = "Manual adjustment",
                JournalEntryLines =
                {
                    new Dto.Financial.JournalEntryLine { AccountId = AccountId("10111"), DrCr = 1, Amount = 25m },
                    new Dto.Financial.JournalEntryLine { AccountId = AccountId("40100"), DrCr = 2, Amount = 25m }
                }
            };
        }
    }
}
