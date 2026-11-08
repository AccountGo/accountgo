#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Sales;
using Core.Tenancy;
using Dto.Banking;
using Dto.Financial;
using Dto.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests
{
    public partial class MultiTenancyPhase3Tests
    {
        [Fact]
        public async Task Acceptance_200LineDayFirstCsv_ImportsAllRowsWithCorrectDates_AndRefusesReimportAsAlreadyImported()
        {
            var bank = await CreateBankOk(new CreateBankAccountRequest { Name = "DayFirstBank", Type = 1 });

            // Generate 200 lines where day is between 13 and 28 so day-first (dd/MM/yyyy) is unambiguous
            var sb = new StringBuilder();
            sb.AppendLine("Date,Payee,Amount,Memo,Reference");
            for (int i = 1; i <= 200; i++)
            {
                int day = 13 + (i % 15); // 13..27
                decimal amount = (i % 2 == 0) ? (100m + i) : -(50m + i);
                sb.AppendLine($"{day:D2}/05/2026,Vendor Or Client {i},{amount:F2},Memo {i},REF-{i:D5}");
            }

            var request = new BankStatementImportRequest
            {
                Source = (int)BankStatementSources.Csv,
                FileName = "transactions-dayfirst.csv",
                Content = sb.ToString()
            };

            // 1. First import: all 200 lines imported
            var res1 = await Send(HttpMethod.Post, $"/api/Banking/Accounts/{bank.Id}/StatementImports", request);
            Assert.True(res1.IsSuccessStatusCode, await res1.Content.ReadAsStringAsync());
            var commit1 = await res1.Content.ReadFromJsonAsync<BankStatementCommitResultDto>();
            Assert.NotNull(commit1);
            Assert.Equal(200, commit1.Imported);
            Assert.Equal(0, commit1.Errors);
            Assert.Equal(0, commit1.Duplicates);

            // Verify lines in database have correct parsed date (Month 5, May 2026)
            var linesRes = await Send(HttpMethod.Get, $"/api/Banking/Accounts/{bank.Id}/StatementLines?pageSize=200");
            Assert.True(linesRes.IsSuccessStatusCode, await linesRes.Content.ReadAsStringAsync());
            var linesList = await linesRes.Content.ReadFromJsonAsync<BankStatementLineListDto>();
            Assert.NotNull(linesList);
            Assert.Equal(200, linesList.Lines.Count);
            Assert.All(linesList.Lines, line =>
            {
                Assert.Equal(2026, line.PostedDate.Year);
                Assert.Equal(5, line.PostedDate.Month);
                Assert.True(line.PostedDate.Day >= 13 && line.PostedDate.Day <= 27);
            });

            // 2. Re-import identical file: detects all 200 rows as duplicates
            var res2 = await Send(HttpMethod.Post, $"/api/Banking/Accounts/{bank.Id}/StatementImports", request);
            if (res2.IsSuccessStatusCode)
            {
                var commit2 = await res2.Content.ReadFromJsonAsync<BankStatementCommitResultDto>();
                Assert.NotNull(commit2);
                Assert.Equal(0, commit2.Imported);
                Assert.Equal(200, commit2.Duplicates);
            }
            else
            {
                Assert.Equal(HttpStatusCode.BadRequest, res2.StatusCode);
                var err = await res2.Content.ReadAsStringAsync();
                Assert.Contains("already imported", err, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public async Task Acceptance_ProWithFinancialsOff_RecordsPaidExpense_AndVoidUnmatchesStatementLine()
        {
            var bank = await CreateBankOk(new CreateBankAccountRequest { Name = "Pro Freelancer Bank", Type = 1 });
            var lineId = AddStatementPayment(bank.Id, -75.00m, "Hosting Provider", DateTime.UtcNow.Date);

            // Simulate Invoicing Pro with Financials module turned off
            WithDb(db =>
            {
                var c = db.Companies.Single(c => c.Id == _companyId);
                c.DisabledModulesJson = "[\"financials\"]";
                db.SaveChanges();
            });

            // 1. Can retrieve statement expense categories without Financials module
            var catRes = await Send(HttpMethod.Get, "/api/Banking/StatementExpenseCategories");
            Assert.True(catRes.IsSuccessStatusCode, await catRes.Content.ReadAsStringAsync());
            var categories = await catRes.Content.ReadFromJsonAsync<StatementExpenseCategoriesDto>();
            Assert.NotNull(categories);
            var category = categories.Curated.Concat(categories.More).First();

            // 2. Record expense from statement line creates paid bill
            var recRes = await Send(HttpMethod.Post, $"/api/Banking/StatementLines/{lineId}/RecordExpense",
                new RecordExpenseFromStatementLineRequest
                {
                    NewVendor = new QuickVendorFromStatementRequest { Name = "Hosting Provider" },
                    ExpenseAccountId = category.AccountId
                });
            Assert.True(recRes.IsSuccessStatusCode, await recRes.Content.ReadAsStringAsync());

            int billId = 0;
            int? matchedGlLineId = null;
            WithDb(db =>
            {
                var line = db.BankStatementLines.Single(l => l.Id == lineId);
                Assert.NotNull(line.MatchedGeneralLedgerLineId);
                matchedGlLineId = line.MatchedGeneralLedgerLineId;

                var bill = db.PurchaseInvoiceHeaders.Single(b => b.Vendor.Party.Name == "Hosting Provider");
                Assert.Equal(75.00m, bill.TotalAmount);
                billId = bill.Id;
            });

            // 3. Voiding the bill reverses GL and returns statement line to Unmatched
            var voidRes = await Send(HttpMethod.Post, $"/api/Purchasing/VoidBill?id={billId}&date={IsoDate(DateTime.UtcNow)}&reason=duplicate");
            Assert.True(voidRes.IsSuccessStatusCode, await voidRes.Content.ReadAsStringAsync());

            WithDb(db =>
            {
                var line = db.BankStatementLines.Single(l => l.Id == lineId);
                Assert.Null(line.MatchedGeneralLedgerLineId);
            });
        }

        [Fact]
        public async Task Acceptance_InvoiceMatching_AutoMatchesWithinThreeDays()
        {
            var bank = await CreateBankOk(new CreateBankAccountRequest { Name = "Matching Bank", Type = 1 });
            var customer = await SaveCustomer(new CustomerDto { Name = "Match Client Inc" });

            // Post customer invoice on 2026-07-10
            var invoiceDate = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
            var inv = await SaveInvoice(new SalesInvoiceDto
            {
                CustomerId = customer.Id,
                Date = invoiceDate,
                Lines = new List<SalesInvoiceLineDto>
                {
                    new SalesInvoiceLineDto { Description = "Consulting", Quantity = 1, UnitPrice = 320m, Amount = 320m }
                }
            });
            Assert.True((await Send(HttpMethod.Post, $"/api/Sales/PostSalesInvoice?id={inv.Id}")).IsSuccessStatusCode);

            // Apply a receipt deposited to the bank GL account on 2026-07-10
            var receiptRes = await Send(HttpMethod.Post, "/api/Sales/SaveSalesReceipt", new SalesReceiptHeaderDto
            {
                CustomerId = customer.Id,
                BankAccountId = bank.Id,
                Date = invoiceDate,
                Amount = 320m,
                SalesInvoiceId = inv.Id
            });
            Assert.True(receiptRes.IsSuccessStatusCode, await receiptRes.Content.ReadAsStringAsync());

            // Statement line arrives with date 2026-07-12 (2 days later, within the 3-day match window)
            var csv = $"Date,Payee,Amount,Memo\n12/07/2026,Match Client Inc,320.00,INV-{inv.Id} Payment\n";
            var importRes = await Send(HttpMethod.Post, $"/api/Banking/Accounts/{bank.Id}/StatementImports",
                new BankStatementImportRequest
                {
                    Source = (int)BankStatementSources.Csv,
                    FileName = "deposit.csv",
                    Content = csv
                });
            Assert.True(importRes.IsSuccessStatusCode, await importRes.Content.ReadAsStringAsync());
            var commit = await importRes.Content.ReadFromJsonAsync<BankStatementCommitResultDto>();
            Assert.NotNull(commit);
            Assert.Equal(1, commit.Imported);
            Assert.Equal(1, commit.AutoMatched);

            WithDb(db =>
            {
                var stmtLine = db.BankStatementLines.Single(l => l.BankId == bank.Id && l.Amount == 320m);
                Assert.NotNull(stmtLine.MatchedGeneralLedgerLineId);
            });
        }

        [Fact]
        public async Task Acceptance_Exclusion_LeavesTrialBalanceIdentical()
        {
            var bank = await CreateBankOk(new CreateBankAccountRequest { Name = "Exclusion Bank", Type = 1 });
            var lineId = AddStatementPayment(bank.Id, -250.00m, "Personal Withdrawal", DateTime.UtcNow.Date);

            // Get trial balance before exclusion
            var tbRes1 = await Send(HttpMethod.Get, "/api/Financials/trialbalance");
            Assert.True(tbRes1.IsSuccessStatusCode, await tbRes1.Content.ReadAsStringAsync());
            var tbBefore = await tbRes1.Content.ReadAsStringAsync();

            // Exclude statement line as Personal
            var exRes = await Send(HttpMethod.Post, $"/api/Banking/StatementLines/{lineId}/Exclude",
                new ExcludeBankStatementLineRequest
                {
                    Reason = (int)BankStatementExclusionReasons.Personal
                });
            Assert.True(exRes.IsSuccessStatusCode, await exRes.Content.ReadAsStringAsync());

            // Get trial balance after exclusion
            var tbRes2 = await Send(HttpMethod.Get, "/api/Financials/trialbalance");
            Assert.True(tbRes2.IsSuccessStatusCode, await tbRes2.Content.ReadAsStringAsync());
            var tbAfter = await tbRes2.Content.ReadAsStringAsync();

            // Trial balance is completely identical (no GL lines were created)
            Assert.Equal(tbBefore, tbAfter);

            WithDb(db =>
            {
                var line = db.BankStatementLines.Single(l => l.Id == lineId);
                Assert.NotNull(line.ExcludedAt);
                Assert.Equal(BankStatementExclusionReasons.Personal, line.ExclusionReason);
            });
        }

        [Fact]
        public async Task Acceptance_CrossTenantReference_ReturnsDenied()
        {
            // Bank and statement line in Company 1
            var bank = await CreateBankOk(new CreateBankAccountRequest { Name = "Tenant 1 Bank", Type = 1 });
            var lineId = AddStatementPayment(bank.Id, -99.00m, "Cross Tenant Vendor", DateTime.UtcNow.Date);

            // Create Company 2 using other user
            var c2Res = await Send(HttpMethod.Post, "/api/organizations",
                new { name = "Tenant Two Co", slug = "tenant-two-co" },
                token: _otherToken, scoped: false);
            Assert.Equal(HttpStatusCode.Created, c2Res.StatusCode);
            var c2Json = await Json(c2Res);
            int company2Id = int.Parse(c2Json.GetProperty("id").GetString());

            // Find an account belonging strictly to Company 2
            int company2AccountId = 0;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                company2AccountId = db.Accounts.First(a => a.CompanyId == company2Id && a.AccountClassId == (int)AccountClasses.Expense).Id;
            }

            // Tenant 1 attempts to record expense using Tenant 2's account
            var recRes = await Send(HttpMethod.Post, $"/api/Banking/StatementLines/{lineId}/RecordExpense",
                new RecordExpenseFromStatementLineRequest
                {
                    NewVendor = new QuickVendorFromStatementRequest { Name = "Cross Tenant Vendor" },
                    ExpenseAccountId = company2AccountId
                });

            // Must reject cross-tenant reference
            Assert.Equal(HttpStatusCode.BadRequest, recRes.StatusCode);
            var errJson = await recRes.Content.ReadAsStringAsync();
            Assert.Contains("CROSS_TENANT_REFERENCE_DENIED", errJson);
        }

        [Fact]
        public async Task Acceptance_TraditionalAccounting_RegressionFlow_RecordsOtherIncomeWithTaxAndPosts()
        {
            var bank = await CreateBankOk(new CreateBankAccountRequest { Name = "Accounting Bank", Type = 1 });
            var lineId = AddStatementPayment(bank.Id, 120.00m, "Direct Client Deposit", DateTime.UtcNow.Date);

            // Get income categories
            var catRes = await Send(HttpMethod.Get, "/api/Banking/StatementIncomeCategories");
            Assert.True(catRes.IsSuccessStatusCode, await catRes.Content.ReadAsStringAsync());
            var categories = await catRes.Content.ReadFromJsonAsync<StatementIncomeCategoriesDto>();
            Assert.NotNull(categories);
            var category = categories.Curated.Concat(categories.More).First();

            // Find active tax
            int? taxId = null;
            WithDb(db =>
            {
                var tax = db.Taxes.FirstOrDefault(t => t.CompanyId == _companyId && t.IsActive && t.SalesAccountId != null);
                if (tax != null) taxId = tax.Id;
            });

            // Record other income with tax
            var recRes = await Send(HttpMethod.Post, $"/api/Banking/StatementLines/{lineId}/RecordOtherIncome",
                new RecordOtherIncomeRequestDto
                {
                    IncomeAccountId = category.AccountId,
                    TaxId = taxId,
                    Description = "Client consulting fee",
                    Memo = "Direct statement income"
                });
            Assert.True(recRes.IsSuccessStatusCode, await recRes.Content.ReadAsStringAsync());

            WithDb(db =>
            {
                var line = db.BankStatementLines.Single(l => l.Id == lineId);
                Assert.NotNull(line.MatchedGeneralLedgerLineId);

                // Sales invoice was issued, approved, and paid
                var inv = db.SalesInvoiceHeaders.Include(i => i.SalesInvoiceLines)
                    .Single(i => i.GeneralLedgerHeaderId != null && i.CustomerAllocations.Any());
                Assert.Equal(120.00m, inv.TotalAmount);
                if (taxId.HasValue)
                {
                    Assert.True(inv.SalesInvoiceLines.First().TaxAmount > 0m);
                }
            });

            // Financial reports generate cleanly
            var tbRes = await Send(HttpMethod.Get, "/api/Financials/trialbalance");
            Assert.True(tbRes.IsSuccessStatusCode, await tbRes.Content.ReadAsStringAsync());

            var bsRes = await Send(HttpMethod.Get, "/api/Financials/balancesheet");
            Assert.True(bsRes.IsSuccessStatusCode, await bsRes.Content.ReadAsStringAsync());
        }
    }
}
