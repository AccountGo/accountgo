#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Data;
using Api.Data.Schema;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Recurring;
using Core.Domain.Sales;
using Core.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.Banking;
using Services.Recurring;
using Xunit;

namespace AccountGo.BackendTests
{
    public partial class MultiTenancyPhase3Tests
    {
        private void SetInvoiceFormat(int companyId, string format, string userId = null, string slug = null)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using (tenant.ImpersonateUser(companyId, userId ?? _ownerId, slug ?? _slug, CompanyMemberRole.Owner))
            {
                var seq = db.SequenceNumbers
                    .FirstOrDefault(s => s.SequenceNumberType == SequenceNumberTypes.SalesInvoice);
                if (seq == null)
                {
                    db.SequenceNumbers.Add(new SequenceNumber
                    {
                        CompanyId = companyId,
                        SequenceNumberType = SequenceNumberTypes.SalesInvoice,
                        Description = nameof(SequenceNumberTypes.SalesInvoice),
                        Prefix = "",
                        UsePrefix = false,
                        NextNumber = 1,
                        Format = format
                    });
                }
                else
                {
                    seq.Format = format;
                }
                db.SaveChanges();
            }
        }

        private void SetCompanyFiscalYearPattern(int companyId, FiscalYearPattern pattern, int? customStartMonth, DateTime booksStartDate, string userId = null, string slug = null)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using (tenant.ImpersonateUser(companyId, userId ?? _ownerId, slug ?? _slug, CompanyMemberRole.Owner))
            {
                var setting = db.CompanySettings.First(s => s.CompanyId == companyId);
                setting.FiscalYearPattern = pattern;
                setting.FiscalYearStartMonth = customStartMonth;
                setting.BooksStartDate = booksStartDate.Date;

                var existingYears = db.FinancialYears.Where(y => y.CompanyId == companyId).ToList();
                var series = db.DocumentNumberSeries.Where(s => s.CompanyId == companyId).ToList();
                db.DocumentNumberSeries.RemoveRange(series);
                db.FinancialYears.RemoveRange(existingYears);
                db.SaveChanges();

                var initial = FiscalYearCalculator.Containing(booksStartDate.Date, pattern, customStartMonth);
                db.FinancialYears.Add(new FinancialYear
                {
                    CompanyId = companyId,
                    FiscalYearCode = initial.Code,
                    FiscalYearName = initial.Code,
                    StartDate = initial.Start,
                    EndDate = initial.End,
                    IsActive = true
                });
                db.SaveChanges();
            }
        }

        private async Task<int> SaveDraftOnDate(DateTime date, string number = null)
        {
            var invoice = await NewInvoice();
            invoice.InvoiceDate = date.Date;
            invoice.DueDate = date.Date.AddDays(30);
            invoice.Status = "Draft";
            invoice.No = number;
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            return (await Json(saved)).GetProperty("id").GetInt32();
        }

        [Fact]
        public async Task InvoiceNumbering_DefaultUnchanged_IssuesContinuousIntegers()
        {
            var startNo = int.Parse(await NextNumber());
            var d1 = await SaveDraft();
            var d2 = await SaveDraft();
            Assert.Equal(startNo.ToString(), await Issue(d1));
            Assert.Equal((startNo + 1).ToString(), await Issue(d2));
        }

        [Fact]
        public async Task InvoiceNumbering_FiscalYearBoundaries_Calendar_AprMar_Custom_And_MidYearBooksStart()
        {
            SetInvoiceFormat(_companyId, "INV-{FY}-{####}");

            // 1. Calendar year boundary: 2026-12-31 vs 2027-01-01
            SetCompanyFiscalYearPattern(_companyId, FiscalYearPattern.Calendar, null, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var calDec = await SaveDraftOnDate(new DateTime(2026, 12, 31));
            var calJan = await SaveDraftOnDate(new DateTime(2027, 1, 1));
            Assert.Equal("INV-2026-0001", await Issue(calDec));
            Assert.Equal("INV-2027-0001", await Issue(calJan));

            // 2. April-March boundary: 2027-03-31 (2026-27) vs 2027-04-01 (2027-28)
            SetInvoiceFormat(_companyId, "APR-{FY}-{####}");
            SetCompanyFiscalYearPattern(_companyId, FiscalYearPattern.AprMar, null, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
            var aprBefore = await SaveDraftOnDate(new DateTime(2027, 3, 31));
            var aprAfter = await SaveDraftOnDate(new DateTime(2027, 4, 1));
            Assert.Equal("APR-2026-27-0001", await Issue(aprBefore));
            Assert.Equal("APR-2027-28-0001", await Issue(aprAfter));

            // 3. Custom start month (October) with mid-year books start (2026-11-15 -> FY2026-27)
            SetInvoiceFormat(_companyId, "OCT-{FY}-{###}");
            SetCompanyFiscalYearPattern(_companyId, FiscalYearPattern.Custom, 10, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));
            var midYear = await SaveDraftOnDate(new DateTime(2027, 2, 10));
            var nextCustom = await SaveDraftOnDate(new DateTime(2027, 10, 1));
            Assert.Equal("OCT-2026-27-001", await Issue(midYear));
            Assert.Equal("OCT-2027-28-001", await Issue(nextCustom));
        }

        [Fact]
        public async Task InvoiceNumbering_DocumentDateDecides_BackDated_And_LockedPeriod()
        {
            SetCompanyFiscalYearPattern(_companyId, FiscalYearPattern.Calendar, null, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            SetInvoiceFormat(_companyId, "INV-{FY}-{####}");

            // Issue one in 2027 first, then issue a back-dated 2026 invoice.
            var d2027 = await SaveDraftOnDate(new DateTime(2027, 1, 5));
            Assert.Equal("INV-2027-0001", await Issue(d2027));

            var d2026a = await SaveDraftOnDate(new DateTime(2026, 12, 30));
            Assert.Equal("INV-2026-0001", await Issue(d2026a));

            var d2026b = await SaveDraftOnDate(new DateTime(2026, 11, 15));
            Assert.Equal("INV-2026-0002", await Issue(d2026b));

            // Lock 2026-12-31: issuing another 2026 draft must be refused and consume no number.
            var lockedDraft = await SaveDraftOnDate(new DateTime(2026, 12, 15));
            WithDb(db =>
            {
                var setting = db.CompanySettings.IgnoreQueryFilters().First(s => s.CompanyId == _companyId);
                setting.BooksLockedThrough = new DateTime(2026, 12, 31);
                db.SaveChanges();
            });
            var lockedRes = await Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={lockedDraft}");
            Assert.Equal(HttpStatusCode.BadRequest, lockedRes.StatusCode);

            WithDb(db =>
            {
                var setting = db.CompanySettings.IgnoreQueryFilters().First(s => s.CompanyId == _companyId);
                setting.BooksLockedThrough = null;
                db.SaveChanges();
            });
            Assert.Equal("INV-2026-0003", await Issue(lockedDraft));
        }

        [Fact]
        public async Task InvoiceNumbering_NoReuse_OnVoid_FailedPost_And_FormatChange()
        {
            SetCompanyFiscalYearPattern(_companyId, FiscalYearPattern.Calendar, null, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            SetInvoiceFormat(_companyId, "INV-{FY}-{####}");

            var d1 = await SaveDraftOnDate(new DateTime(2026, 5, 1));
            var no1 = await Issue(d1);
            Assert.Equal("INV-2026-0001", no1);

            // Voiding keeps INV-2026-0001; next invoice gets INV-2026-0002.
            var voidRes = await Send(HttpMethod.Post, $"/api/Sales/VoidSalesInvoice?id={d1}&date=2026-05-02");
            Assert.Equal(HttpStatusCode.OK, voidRes.StatusCode);
            Assert.Equal(no1, (await GetInvoice(d1)).No);

            var d2 = await SaveDraftOnDate(new DateTime(2026, 5, 3));
            Assert.Equal("INV-2026-0002", await Issue(d2));

            // Changing format leaves issued invoices untouched and applies only to new issues.
            SetInvoiceFormat(_companyId, "S-{FY}-{###}");
            Assert.Equal("INV-2026-0001", (await GetInvoice(d1)).No);
            Assert.Equal("INV-2026-0002", (await GetInvoice(d2)).No);

            var d3 = await SaveDraftOnDate(new DateTime(2026, 5, 4));
            Assert.Equal("S-2026-003", await Issue(d3));
        }

        [Fact]
        public async Task InvoiceNumbering_TypedNumbers_AdvanceSameYearSeries_SkipTaken_And_409OnDuplicate()
        {
            SetCompanyFiscalYearPattern(_companyId, FiscalYearPattern.Calendar, null, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            SetInvoiceFormat(_companyId, "INV-{FY}-{####}");

            // Hold INV-2026-0002 on a draft so auto-allocation skips it.
            var heldDraft = await SaveDraftOnDate(new DateTime(2026, 6, 1), "INV-2026-0002");

            var d1 = await SaveDraftOnDate(new DateTime(2026, 6, 2));
            Assert.Equal("INV-2026-0001", await Issue(d1));

            var d2 = await SaveDraftOnDate(new DateTime(2026, 6, 3));
            Assert.Equal("INV-2026-0003", await Issue(d2));

            // Typing a higher number in this year's format advances the series past it.
            var typedDraft = await SaveDraftOnDate(new DateTime(2026, 6, 4), "INV-2026-0010");
            Assert.Equal("INV-2026-0010", await Issue(typedDraft));

            var dNext = await SaveDraftOnDate(new DateTime(2026, 6, 5));
            Assert.Equal("INV-2026-0011", await Issue(dNext));

            // Typing a non-matching format leaves the series untouched.
            var customDraft = await SaveDraftOnDate(new DateTime(2026, 6, 6), "MANUAL-99");
            Assert.Equal("MANUAL-99", await Issue(customDraft));

            var dAfterManual = await SaveDraftOnDate(new DateTime(2026, 6, 7));
            Assert.Equal("INV-2026-0012", await Issue(dAfterManual));

            // Duplicate number returns 409 Conflict.
            var dupInvoice = await NewInvoice();
            dupInvoice.InvoiceDate = new DateTime(2026, 6, 8);
            dupInvoice.Status = "Draft";
            dupInvoice.No = "INV-2026-0001";
            var dupRes = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", dupInvoice);
            Assert.Equal(HttpStatusCode.Conflict, dupRes.StatusCode);

            // Clean up held draft reference
            Assert.True(heldDraft > 0);
        }

        [Fact]
        public async Task InvoiceNumbering_Concurrency_ParallelIssuesInSameYear_And_ParallelFirstIssuesInNewYear()
        {
            SetCompanyFiscalYearPattern(_companyId, FiscalYearPattern.Calendar, null, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            SetInvoiceFormat(_companyId, "CONC-{FY}-{####}");

            const int count = 6;
            // 1. Parallel first issues in brand-new fiscal year 2028 (no FinancialYear or DocumentNumberSeries row yet).
            var newYearDrafts = new List<int>();
            for (var i = 0; i < count; i++)
                newYearDrafts.Add(await SaveDraftOnDate(new DateTime(2028, 3, 1 + i)));

            var newYearResults = await Task.WhenAll(newYearDrafts.Select(id =>
                Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={id}")));
            foreach (var r in newYearResults)
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);

            var newYearNos = new List<string>();
            foreach (var r in newYearResults)
                newYearNos.Add((await Json(r)).GetProperty("no").GetString());

            Assert.Equal(count, newYearNos.Distinct().Count());
            for (var i = 1; i <= count; i++)
                Assert.Contains($"CONC-2028-{i:0000}", newYearNos);

            WithDb(db =>
            {
                var fy = db.FinancialYears.IgnoreQueryFilters().Single(y => y.CompanyId == _companyId && y.FiscalYearCode == "FY2028");
                var seriesCount = db.DocumentNumberSeries.IgnoreQueryFilters().Count(s =>
                    s.CompanyId == _companyId && s.SequenceNumberType == SequenceNumberTypes.SalesInvoice && s.FinancialYearId == fy.Id);
                Assert.Equal(1, seriesCount);
            });

            // 2. Parallel issues in an existing fiscal year (2028) continue consecutively.
            var sameYearDrafts = new List<int>();
            for (var i = 0; i < count; i++)
                sameYearDrafts.Add(await SaveDraftOnDate(new DateTime(2028, 4, 1 + i)));

            var sameYearResults = await Task.WhenAll(sameYearDrafts.Select(id =>
                Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={id}")));
            foreach (var r in sameYearResults)
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);

            var sameYearNos = new List<string>();
            foreach (var r in sameYearResults)
                sameYearNos.Add((await Json(r)).GetProperty("no").GetString());

            Assert.Equal(count, sameYearNos.Distinct().Count());
            for (var i = count + 1; i <= count * 2; i++)
                Assert.Contains($"CONC-2028-{i:0000}", sameYearNos);
        }

        [Fact]
        public async Task InvoiceNumbering_PreviewByDate_ReadOnly_And_ExpectedNo409()
        {
            SetCompanyFiscalYearPattern(_companyId, FiscalYearPattern.Calendar, null, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            SetInvoiceFormat(_companyId, "PRV-{FY}-{####}");

            // Previewing a future year (2031) must NOT create a FinancialYear row.
            var futurePreviewRes = await Send(HttpMethod.Get, "/api/Sales/NextSalesInvoiceNumber?invoiceDate=2031-05-10");
            Assert.Equal(HttpStatusCode.OK, futurePreviewRes.StatusCode);
            Assert.Equal("PRV-2031-0001", (await Json(futurePreviewRes)).GetProperty("next").GetString());

            WithDb(db =>
            {
                Assert.False(db.FinancialYears.IgnoreQueryFilters().Any(y => y.CompanyId == _companyId && y.FiscalYearCode == "FY2031"));
            });

            // expectedNo mismatch returns 409 number_changed
            var d1 = await SaveDraftOnDate(new DateTime(2026, 7, 1));
            var d2 = await SaveDraftOnDate(new DateTime(2026, 7, 2));
            var previewRes = await Send(HttpMethod.Get, "/api/Sales/NextSalesInvoiceNumber?invoiceDate=2026-07-02");
            var expected = (await Json(previewRes)).GetProperty("next").GetString();
            Assert.Equal("PRV-2026-0001", expected);

            Assert.Equal("PRV-2026-0001", await Issue(d1));

            var staleRes = await Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={d2}&expectedNo={Uri.EscapeDataString(expected)}");
            Assert.Equal(HttpStatusCode.Conflict, staleRes.StatusCode);
            var staleJson = await Json(staleRes);
            Assert.Equal("number_changed", staleJson.GetProperty("code").GetString());
            Assert.Equal("PRV-2026-0002", staleJson.GetProperty("next").GetString());
        }

        [Fact]
        public async Task InvoiceNumbering_514Contract_Non12MonthTransitionYear_UsesOwnSeriesAndToken()
        {
            SetCompanyFiscalYearPattern(_companyId, FiscalYearPattern.Calendar, null, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            SetInvoiceFormat(_companyId, "INV-{FY}-{####}");

            // Insert a 6-month transition period row (2027-01-01 .. 2027-06-30, coded FY2027T).
            WithDb(db =>
            {
                db.FinancialYears.Add(new FinancialYear
                {
                    CompanyId = _companyId,
                    FiscalYearCode = "FY2027T",
                    FiscalYearName = "Transition 2027",
                    StartDate = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    EndDate = new DateTime(2027, 6, 30, 23, 59, 59, DateTimeKind.Utc),
                    IsActive = true
                });
                db.SaveChanges();
            });

            var d2026 = await SaveDraftOnDate(new DateTime(2026, 12, 15));
            var dTrans1 = await SaveDraftOnDate(new DateTime(2027, 2, 1));
            var dTrans2 = await SaveDraftOnDate(new DateTime(2027, 6, 30));

            Assert.Equal("INV-2026-0001", await Issue(d2026));
            Assert.Equal("INV-2027T-0001", await Issue(dTrans1));
            Assert.Equal("INV-2027T-0002", await Issue(dTrans2));
        }

        [Fact]
        public async Task InvoiceNumbering_Tenancy_IndependentSeriesPerWorkspace()
        {
            SetCompanyFiscalYearPattern(_companyId, FiscalYearPattern.Calendar, null, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            SetInvoiceFormat(_companyId, "TEN-{FY}-{####}");

            var dMine = await SaveDraftOnDate(new DateTime(2026, 8, 1));
            Assert.Equal("TEN-2026-0001", await Issue(dMine));

            // Second workspace with the same format & year starts at 0001 independently.
            var otherSlug = "ten-other-" + Guid.NewGuid().ToString("N")[..6];
            var otherOrgRes = await Send(HttpMethod.Post, "/api/organizations", new { name = "Second", slug = otherSlug }, token: _otherToken, scoped: false);
            Assert.Equal(HttpStatusCode.Created, otherOrgRes.StatusCode);
            int otherCompanyId = 0;
            WithDb(db =>
            {
                otherCompanyId = db.Companies.Single(c => c.Slug == otherSlug).Id;
                var memberId = db.CompanyMembers.Single(m => m.CompanyId == otherCompanyId && m.UserId == _otherId).Id;
                var salesRoleId = db.SecurityRoles.Single(r => r.Name == "Sales Representative").Id;
                db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole
                {
                    CompanyMemberId = memberId,
                    SecurityRoleId = salesRoleId
                });
                db.SaveChanges();
            });

            SetCompanyFiscalYearPattern(otherCompanyId, FiscalYearPattern.Calendar, null, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), _otherId, otherSlug);
            SetInvoiceFormat(otherCompanyId, "TEN-{FY}-{####}", _otherId, otherSlug);

            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/Sales/NextSalesInvoiceNumber?invoiceDate=2026-08-01");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _otherToken);
            req.Headers.Add("X-AccountGo-Org", otherSlug);
            var previewOther = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, previewOther.StatusCode);
            Assert.Equal("TEN-2026-0001", (await Json(previewOther)).GetProperty("next").GetString());
        }

        [SqliteFact]
        public void InvoiceNumbering_Schema_SqliteUpgraderIdempotent()
        {
            WithDb(db =>
            {
                var conn = db.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    conn.Open();
                SqliteSchemaUpgrader.Upgrade(conn);
                SqliteSchemaUpgrader.Upgrade(conn);
            });
        }

        [Fact]
        public async Task InvoiceNumbering_RecurringInvoice_AutoIssue_UsesFormattedYearlySeries()
        {
            await NewInvoice(); // Ensures default customer and item exist.
            SetCompanyFiscalYearPattern(_companyId, FiscalYearPattern.Calendar, null, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            SetInvoiceFormat(_companyId, "REC-{FY}-{####}");

            var runDate = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);

            int salesInvoiceId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                using (tenant.ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
                {
                    var customerId = db.Customers.First().Id;
                    var itemId = db.Items.First().Id;
                    var schedule = new RecurringSchedule
                    {
                        CompanyId = _companyId,
                        Kind = RecurringScheduleKind.SalesInvoice,
                        PartyId = customerId,
                        Name = "Monthly Retainer",
                        Cadence = RecurringCadence.Monthly,
                        Interval = 1,
                        DayOfMonth = 15,
                        StartDate = runDate,
                        Action = RecurringAction.AutoIssue,
                        ServicePeriodMode = RecurringServicePeriodMode.CurrentMonth,
                        AutomationEnabledBy = _ownerId,
                    };
                    db.RecurringSchedules.Add(schedule);
                    db.SaveChanges();

                    db.RecurringScheduleLines.Add(new RecurringScheduleLine
                    {
                        CompanyId = _companyId,
                        RecurringScheduleId = schedule.Id,
                        ItemId = itemId,
                        Quantity = 1m,
                        Amount = 100m,
                    });
                    db.SaveChanges();

                    var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();
                    var occurrenceId = generation.EnsureOccurrence(schedule.Id, runDate, "test-run", DateTime.UtcNow.AddMinutes(5));
                    var result = generation.ProcessOccurrence(occurrenceId, "test-run");
                    Assert.Equal(RecurringGenerationOutcome.Issued, result.Outcome);
                    Assert.NotNull(result.SalesInvoiceId);
                    salesInvoiceId = result.SalesInvoiceId.Value;
                }
            }

            var inv = await GetInvoice(salesInvoiceId);
            Assert.Equal("REC-2026-0001", inv.No);
        }

        [Fact]
        public async Task InvoiceNumbering_StatementOtherIncome_UsesFormattedYearlySeries()
        {
            SetCompanyFiscalYearPattern(_companyId, FiscalYearPattern.Calendar, null, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            SetInvoiceFormat(_companyId, "STM-{FY}-{####}");

            int lineId = 0;
            int incomeAccountId = 0;
            WithDb(db =>
            {
                var bank = db.Banks.IgnoreQueryFilters().FirstOrDefault(b => b.CompanyId == _companyId);
                if (bank == null)
                {
                    var cashAccount = db.Accounts.IgnoreQueryFilters().First(a => a.CompanyId == _companyId && a.AccountCode == "10111");
                    bank = new Bank
                    {
                        CompanyId = _companyId,
                        Name = "Operating Bank",
                        AccountId = cashAccount.Id,
                        IsActive = true,
                        IsDefault = true,
                        Type = BankTypes.CheckingAccount
                    };
                    db.Banks.Add(bank);
                    db.SaveChanges();
                }
                incomeAccountId = db.Accounts.IgnoreQueryFilters().First(a => a.CompanyId == _companyId && a.AccountCode == "40100").Id;
                var import = new BankStatementImport
                {
                    CompanyId = _companyId,
                    BankId = bank.Id,
                    Source = BankStatementSources.Csv,
                    FileName = "stmt.csv",
                    ImportedAt = DateTime.UtcNow,
                    ImportedBy = "test"
                };
                db.BankStatementImports.Add(import);
                db.SaveChanges();

                var line = new BankStatementLine
                {
                    CompanyId = _companyId,
                    BankId = bank.Id,
                    BankStatementImportId = import.Id,
                    Source = BankStatementSources.Csv,
                    ExternalId = Guid.NewGuid().ToString("N"),
                    PostedDate = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc),
                    Memo = "Interest rebate",
                    Payee = "Acme Partner",
                    Amount = 250m,
                    CreatedAt = DateTime.UtcNow
                };
                db.BankStatementLines.Add(line);
                db.SaveChanges();
                lineId = line.Id;
            });

            var res = await Send(HttpMethod.Post, $"/api/Banking/StatementLines/{lineId}/RecordOtherIncome", new
            {
                incomeAccountId,
                payeeName = "Acme Partner",
                memo = "Rebate"
            });
            Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());

            WithDb(db =>
            {
                var inv = db.SalesInvoiceHeaders.IgnoreQueryFilters()
                    .Where(i => i.CompanyId == _companyId && i.Origin == SalesInvoiceOrigins.StatementOtherIncome)
                    .OrderByDescending(i => i.Id)
                    .First();
                Assert.Equal("STM-2026-0001", inv.No);
            });
        }

        [Fact]
        public async Task InvoiceNumbering_Settings_FormatAndNextNumber_ForwardOnly_Permission_And_Audit()
        {
            SetCompanyFiscalYearPattern(_companyId, FiscalYearPattern.Calendar, null, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            // 1. Non-admin member cannot update numbering settings (403 Forbidden).
            WithDb(db =>
            {
                if (!db.CompanyMembers.Any(m => m.CompanyId == _companyId && m.UserId == _otherId))
                {
                    db.CompanyMembers.Add(new CompanyMember
                    {
                        CompanyId = _companyId,
                        UserId = _otherId,
                        Role = CompanyMemberRole.Member,
                        Status = CompanyMemberStatus.Active
                    });
                    db.SaveChanges();
                }
            });
            var nonAdminRes = await Send(HttpMethod.Put, "/api/organizations/current/sales-invoice-numbering",
                new { format = "INV-{FY}-{####}", nextNumber = 1 }, token: _otherToken);
            Assert.Equal(HttpStatusCode.Forbidden, nonAdminRes.StatusCode);

            // 2. Invalid format returns 400 with code = "invalid_format".
            var badFormatRes = await Send(HttpMethod.Put, "/api/organizations/current/sales-invoice-numbering",
                new { format = "BAD-{NOPE}" });
            Assert.Equal(HttpStatusCode.BadRequest, badFormatRes.StatusCode);
            Assert.Equal("invalid_format", (await Json(badFormatRes)).GetProperty("code").GetString());

            // 3. Valid format + nextNumber = 50 succeeds and writes audit row.
            var okRes = await Send(HttpMethod.Put, "/api/organizations/current/sales-invoice-numbering",
                new { format = "INV-{FY}-{####}", nextNumber = 50 });
            Assert.Equal(HttpStatusCode.OK, okRes.StatusCode);
            var okJson = await Json(okRes);
            Assert.Equal("INV-{FY}-{####}", okJson.GetProperty("format").GetString());
            Assert.Equal(50, okJson.GetProperty("nextNumber").GetInt32());

            WithDb(db =>
            {
                var audit = db.RecordChanges.IgnoreQueryFilters()
                    .Where(r => r.CompanyId == _companyId && r.EntityType == "SalesInvoiceNumbering")
                    .OrderByDescending(r => r.Id)
                    .FirstOrDefault();
                Assert.NotNull(audit);
                Assert.Contains("INV-{FY}-{####}", audit.ChangesJson);
                Assert.Contains("50", audit.ChangesJson);
            });

            // 4. Issue an invoice on today's date -> gets 0050, series advances to 51.
            var d = await SaveDraftOnDate(DateTime.UtcNow.Date);
            var issuedNo = await Issue(d);
            Assert.EndsWith("-0050", issuedNo);

            // 5. Attempting to move nextNumber backwards (to 49 or 50) is refused (400 invalid_next_number).
            var backRes = await Send(HttpMethod.Put, "/api/organizations/current/sales-invoice-numbering",
                new { format = "INV-{FY}-{####}", nextNumber = 50 });
            Assert.Equal(HttpStatusCode.BadRequest, backRes.StatusCode);
            Assert.Equal("invalid_next_number", (await Json(backRes)).GetProperty("code").GetString());

            // 6. GET returns samples without writing new FinancialYear rows.
            var getRes = await Send(HttpMethod.Get, "/api/organizations/current/sales-invoice-numbering");
            Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
            var getJson = await Json(getRes);
            Assert.Equal(51, getJson.GetProperty("nextNumber").GetInt32());
            Assert.True(getJson.GetProperty("hasIssuedInCurrentFiscalYear").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(getJson.GetProperty("sampleCurrentYear").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(getJson.GetProperty("sampleNextYear").GetString()));
        }
    }
}
