// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AccountGo.BackendTests.Auditing;
using Api.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.Security;
using Core.Domain.TaxSystem;
using Core.Tenancy;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests.TaxSystem;

[Collection("WorkspaceSetupHttp")]
public sealed class TaxFilingHttpTests : IAsyncLifetime
{
    private readonly AuditTestHost _host = new();
    private AuditTestHost.UserSession _owner = null!;
    private int _companyId;
    private const string Org = "tax-filing-http";

    private int _vat12Id;
    private int _red5Id;
    private int _customerId;
    private int _vendorId;
    private int _itemId;
    private int _measurementId;
    private int _expenseAccountId;
    private int _purchasingTaxAccountId;

    public async Task InitializeAsync()
    {
        await _host.InitializeAsync();
        _owner = await _host.CreateUser("tax-filing-owner@example.test");
        _companyId = await _host.CreateWorkspace(_owner.Token, "Tax Filing Co", Org);
        await _host.SetFunctionalRoles(_companyId, _owner.Id, "Accountant");

        WithDb(db =>
        {
            var setting = db.CompanySettings.Single();
            setting.BooksStartDate = new DateTime(2025, 1, 1);
            setting.AccountingBasis = AccountingBasis.Accrual;
            setting.TaxRegime = TaxRegime.VatGst;
            setting.FiscalYearPattern = FiscalYearPattern.Calendar;
            setting.TaxPeriodFrequency = TaxPeriodFrequency.Quarterly;
            setting.TaxQuarterCycleStartMonth = 1;

            foreach (var year in new[] { 2025, 2026, 2027 })
            {
                if (db.FinancialYears.Any(y => y.StartDate.Year == year)) continue;
                db.FinancialYears.Add(new FinancialYear
                {
                    CompanyId = _companyId,
                    FiscalYearCode = year.ToString(CultureInfo.InvariantCulture),
                    FiscalYearName = year.ToString(CultureInfo.InvariantCulture),
                    StartDate = new DateTime(year, 1, 1),
                    EndDate = new DateTime(year, 12, 31),
                    IsActive = true
                });
            }

            var salesTaxAcc = db.Accounts.Single(a => a.AccountCode == "20300");
            var purchaseTaxAcc = db.Accounts.Single(a => a.AccountCode == "10160");
            _purchasingTaxAccountId = purchaseTaxAcc.Id;

            var vat12 = new Core.Domain.TaxSystem.Tax
            {
                CompanyId = _companyId,
                TaxCode = "VAT12",
                TaxName = "VAT 12%",
                Rate = 12m,
                IsActive = true,
                SalesAccountId = salesTaxAcc.Id,
                PurchasingAccountId = purchaseTaxAcc.Id
            };
            var red5 = new Core.Domain.TaxSystem.Tax
            {
                CompanyId = _companyId,
                TaxCode = "RED5",
                TaxName = "Reduced 5%",
                Rate = 5m,
                IsActive = true,
                SalesAccountId = salesTaxAcc.Id,
                PurchasingAccountId = purchaseTaxAcc.Id
            };
            db.Taxes.AddRange(vat12, red5);

            var salesAcc = db.Accounts.Single(a => a.AccountCode == "40100");
            var arAcc = db.Accounts.Single(a => a.AccountCode == "10120");
            var advAcc = db.Accounts.Single(a => a.AccountCode == "20120");
            var apAcc = db.Accounts.Single(a => a.AccountCode == "20110");
            var expAcc = db.Accounts.First(a =>
                a.AccountClassId == (int)AccountClasses.Expense
                && a.ParentAccountId != null
                && !a.IsContraAccount
                && !a.IsCash
                && !db.Accounts.Any(c => c.ParentAccountId == a.Id && c.CompanyId == _companyId));
            _expenseAccountId = expAcc.Id;

            var glSetting = db.GeneralLedgerSettings.First();
            glSetting.ShippingChargeAccountId ??= salesAcc.Id;
            glSetting.PermanentAccountId ??= db.Accounts.Single(a => a.AccountCode == "30200").Id;

            var meas = db.Measurements.First();
            _measurementId = meas.Id;

            var cat = new ItemCategory
            {
                CompanyId = _companyId,
                Name = "Tax filing services",
                ItemType = ItemTypes.Service,
                MeasurementId = meas.Id,
                SalesAccountId = salesAcc.Id
            };
            var item = new Item
            {
                CompanyId = _companyId,
                No = "TAX-FILING-SVC",
                Code = "TAX-FILING-SVC",
                Description = "Tax Filing Service",
                Price = 100m,
                ItemCategory = cat,
                SmallestMeasurementId = meas.Id,
                SalesAccount = salesAcc
            };
            var customer = new Customer
            {
                CompanyId = _companyId,
                No = "TF-CUST-1",
                Party = new Party { CompanyId = _companyId, Name = "Tax Filing Customer", PartyType = PartyTypes.Customer },
                PrimaryContact = new Contact
                {
                    CompanyId = _companyId,
                    FirstName = "Tax",
                    LastName = "Customer",
                    Party = new Party { CompanyId = _companyId, Name = "Tax Filing Contact", PartyType = PartyTypes.Contact }
                },
                AccountsReceivableAccount = arAcc,
                SalesAccount = salesAcc,
                CustomerAdvancesAccount = advAcc
            };
            var vendor = new Vendor
            {
                CompanyId = _companyId,
                No = "TF-VEND-1",
                Party = new Party { CompanyId = _companyId, Name = "Tax Filing Vendor", PartyType = PartyTypes.Vendor },
                PrimaryContact = new Contact
                {
                    CompanyId = _companyId,
                    FirstName = "Tax",
                    LastName = "Vendor",
                    Party = new Party { CompanyId = _companyId, Name = "Vendor Filing Contact", PartyType = PartyTypes.Contact }
                },
                AccountsPayableAccount = apAcc
            };
            db.Items.Add(item);
            db.Customers.Add(customer);
            db.Vendors.Add(vendor);
            db.SaveChanges();

            _vat12Id = vat12.Id;
            _red5Id = red5.Id;
            _itemId = item.Id;
            _customerId = customer.Id;
            _vendorId = vendor.Id;
        });
    }

    public Task DisposeAsync() => _host.DisposeAsync();

    private void WithDb(Action<ApiDbContext> action)
    {
        using var scope = _host.Factory.Services.CreateScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, Org, CompanyMemberRole.Owner);
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        action(db);
        db.SaveChanges();
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private async Task<JsonElement> OkJson(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected 2xx, got {(int)response.StatusCode}: {body}");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private async Task<int> CreatePostedInvoice(DateTime date, params (int? TaxId, decimal Amount, decimal TaxAmount)[] lines)
    {
        var dto = new Dto.Sales.SalesInvoice
        {
            CustomerId = _customerId,
            InvoiceDate = date,
            Status = "Open",
            ShippingHandlingCharge = 0m,
            SalesInvoiceLines = lines.Select(l => new Dto.Sales.SalesInvoiceLine
            {
                ItemId = _itemId,
                MeasurementId = _measurementId,
                Quantity = 1m,
                Amount = l.Amount,
                TaxId = l.TaxId,
                TaxAmount = l.TaxAmount
            }).ToList()
        };
        var res = await _host.Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", dto, _owner.Token, Org);
        var json = await OkJson(res);
        return json.GetProperty("id").GetInt32();
    }

    private async Task<int> CreatePostedBill(DateTime date, params (int? TaxId, decimal Amount, decimal TaxAmount)[] lines)
    {
        var bill = new PurchaseInvoiceHeader
        {
            CompanyId = _companyId,
            VendorId = _vendorId,
            Date = date,
            VendorInvoiceNo = "V-" + Guid.NewGuid().ToString("N")[..6],
            Status = PurchaseInvoiceStatus.Open,
            PurchaseInvoiceLines = lines.Select(l => new PurchaseInvoiceLine
            {
                CompanyId = _companyId,
                ItemId = _itemId,
                AccountId = _expenseAccountId,
                Quantity = 1m,
                Amount = l.Amount,
                TaxId = l.TaxId,
                TaxAmount = l.TaxAmount
            }).ToList()
        };

        var gl = new GeneralLedgerHeader
        {
            CompanyId = _companyId,
            Date = date,
            DocumentType = DocumentTypes.PurchaseInvoice,
            Description = "Test bill " + bill.VendorInvoiceNo
        };
        WithDb(db =>
        {
            var apAccId = db.Accounts.Single(a => a.AccountCode == "20110").Id;
            decimal totalTax = 0m;
            decimal totalSub = 0m;
            foreach (var l in bill.PurchaseInvoiceLines)
            {
                totalSub += l.Subtotal;
                totalTax += l.TaxAmount;
                gl.GeneralLedgerLines.Add(new GeneralLedgerLine
                {
                    CompanyId = _companyId,
                    AccountId = _expenseAccountId,
                    DrCr = DrOrCrSide.Dr,
                    Amount = l.Subtotal
                });
            }
            if (totalTax > 0m)
            {
                gl.GeneralLedgerLines.Add(new GeneralLedgerLine
                {
                    CompanyId = _companyId,
                    AccountId = _purchasingTaxAccountId,
                    DrCr = DrOrCrSide.Dr,
                    Amount = totalTax
                });
            }
            gl.GeneralLedgerLines.Add(new GeneralLedgerLine
            {
                CompanyId = _companyId,
                AccountId = apAccId,
                DrCr = DrOrCrSide.Cr,
                Amount = totalSub + totalTax
            });
            db.GeneralLedgerHeaders.Add(gl);
            db.SaveChanges();
            bill.GeneralLedgerHeaderId = gl.Id;
            bill.PayableAccountId = apAccId;
            bill.No = "BILL-" + gl.Id.ToString(CultureInfo.InvariantCulture);
            db.PurchaseInvoiceHeaders.Add(bill);
        });
        await Task.CompletedTask;
        return bill.Id;
    }

    private async Task<int> PayBill(int billId, decimal amount, DateTime date)
    {
        var res = await _host.Send(HttpMethod.Post, "/api/Purchasing/PayBill",
            new { billId, amount, date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
            _owner.Token, Org);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        int paymentId = 0;
        WithDb(db =>
        {
            paymentId = db.VendorPayments
                .Where(p => p.PurchaseInvoiceHeaderId == billId && p.GeneralLedgerHeaderId != null)
                .OrderByDescending(p => p.Id)
                .First().Id;
        });
        return paymentId;
    }

    private async Task<HttpResponseMessage> PostFiling(
        string periodStart,
        string periodEnd,
        decimal expectedNetTax,
        string? filedOn = null,
        string? reference = "RET-001",
        string? notes = null,
        bool lockBooks = false,
        int? amendsFilingId = null,
        string? amendmentReason = null,
        string? token = null)
    {
        return await _host.Send(HttpMethod.Post, "/api/tax/filings", new
        {
            periodStart,
            periodEnd,
            filedOn = filedOn ?? DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            reference,
            notes,
            lockBooks,
            expectedNetTax,
            amendsFilingId,
            amendmentReason
        }, token ?? _owner.Token, Org);
    }

    [Fact]
    public async Task RecordFiling_EndedQuarter_StoresSnapshotAndAppearsInPeriods_DuplicateReturns409()
    {
        await CreatePostedInvoice(new DateTime(2026, 7, 15), (_vat12Id, 1000m, 120m));
        await CreatePostedBill(new DateTime(2026, 8, 10), (_red5Id, 400m, 20m));

        var reportJson = await OkJson(await _host.Send(
            HttpMethod.Get, "/api/tax/report?from=2026-07-01&to=2026-09-30", null, _owner.Token, Org));
        var netTax = reportJson.GetProperty("netTax").GetDecimal();
        Assert.Equal(100m, netTax);
        Assert.True(reportJson.GetProperty("canRecordFiling").GetBoolean());
        Assert.True(reportJson.GetProperty("canLockOnFiling").GetBoolean());

        var res = await PostFiling("2026-07-01", "2026-09-30", netTax, filedOn: "2026-10-15", reference: "VAT-Q3");
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var filing = await ReadJson(res);
        Assert.Equal(0, filing.GetProperty("sequence").GetInt32());
        Assert.Equal(120m, filing.GetProperty("outputTax").GetDecimal());
        Assert.Equal(20m, filing.GetProperty("inputTax").GetDecimal());
        Assert.Equal(100m, filing.GetProperty("netTax").GetDecimal());

        WithDb(db =>
        {
            var row = db.TaxFilings.Single();
            Assert.Contains("\"taxCode\":\"VAT12\"", row.SnapshotJson);
            Assert.Contains("\"taxCode\":\"RED5\"", row.SnapshotJson);
            Assert.DoesNotContain("\"documents\"", row.SnapshotJson);
        });

        var periods = await OkJson(await _host.Send(HttpMethod.Get, "/api/tax/periods?count=8", null, _owner.Token, Org));
        var q3 = periods.EnumerateArray().Single(p => p.GetProperty("start").GetString() == "2026-07-01");
        var periodFiling = q3.GetProperty("filing");
        Assert.Equal(filing.GetProperty("id").GetInt32(), periodFiling.GetProperty("id").GetInt32());
        Assert.Equal("2026-10-15", periodFiling.GetProperty("filedOn").GetString());
        Assert.False(periodFiling.GetProperty("changedSinceFiling").GetProperty("changed").GetBoolean());

        // Duplicate without amendsFilingId -> 409 tax_filing_exists
        var dup = await PostFiling("2026-07-01", "2026-09-30", netTax);
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal("tax_filing_exists", (await ReadJson(dup)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task DoubleSubmit_ConcurrentPosts_ExactlyOneCreatedAndOneConflict()
    {
        await CreatePostedInvoice(new DateTime(2026, 7, 20), (_vat12Id, 500m, 60m));

        var t1 = PostFiling("2026-07-01", "2026-09-30", 60m, reference: "RACE-1");
        var t2 = PostFiling("2026-07-01", "2026-09-30", 60m, reference: "RACE-2");
        var responses = await Task.WhenAll(t1, t2);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);

        WithDb(db => Assert.Equal(1, db.TaxFilings.Count()));
    }

    [Fact]
    public async Task WrongExpectedNetTax_Returns409FiguresChanged_AndStoresNothing()
    {
        await CreatePostedInvoice(new DateTime(2026, 7, 20), (_vat12Id, 500m, 60m));

        var res = await PostFiling("2026-07-01", "2026-09-30", expectedNetTax: 999m);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("tax_figures_changed", (await ReadJson(res)).GetProperty("code").GetString());

        WithDb(db => Assert.Empty(db.TaxFilings));
    }

    [Fact]
    public async Task CurrentOpenPeriod_Returns400NotEnded()
    {
        var today = DateTime.UtcNow.Date;
        var current = TaxPeriodCalculator.Containing(today, TaxPeriodFrequency.Quarterly, 1);

        var res = await PostFiling(
            current.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            current.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            0m);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("tax_period_not_ended", (await ReadJson(res)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task NonWholePeriod_Returns400NotWhole()
    {
        var res = await PostFiling("2026-07-01", "2026-08-15", 0m);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("tax_period_not_whole", (await ReadJson(res)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Overlap_MonthlyFilingsThenQuarterly_Returns409OverlapsFiling()
    {
        await _host.Send(HttpMethod.Put, "/api/tax/settings",
            new { frequency = "Monthly", quarterCycleStartMonth = (int?)null }, _owner.Token, Org);

        foreach (var (s, e) in new[]
        {
            ("2026-07-01", "2026-07-31"),
            ("2026-08-01", "2026-08-31"),
            ("2026-09-01", "2026-09-30")
        })
        {
            var r = await PostFiling(s, e, 0m);
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        }

        await _host.Send(HttpMethod.Put, "/api/tax/settings",
            new { frequency = "Quarterly", quarterCycleStartMonth = 1 }, _owner.Token, Org);

        var overlap = await PostFiling("2026-07-01", "2026-09-30", 0m);
        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        Assert.Equal("tax_period_overlaps_filing", (await ReadJson(overlap)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Amendments_RequireReasonAndLatestSequence_AndPreserveHistory()
    {
        await CreatePostedInvoice(new DateTime(2026, 7, 10), (_vat12Id, 500m, 60m));
        var origRes = await PostFiling("2026-07-01", "2026-09-30", 60m, reference: "ORIG");
        Assert.Equal(HttpStatusCode.Created, origRes.StatusCode);
        var origId = (await ReadJson(origRes)).GetProperty("id").GetInt32();

        // Missing reason -> 400
        var noReason = await PostFiling("2026-07-01", "2026-09-30", 60m,
            amendsFilingId: origId, amendmentReason: "   ");
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal("tax_amendment_reason_required", (await ReadJson(noReason)).GetProperty("code").GetString());

        // Valid amendment -> Sequence 1
        await CreatePostedInvoice(new DateTime(2026, 7, 25), (_vat12Id, 500m, 60m));
        var amend1Res = await PostFiling("2026-07-01", "2026-09-30", 120m,
            reference: "AMD-1", amendsFilingId: origId, amendmentReason: "Included late invoice");
        Assert.Equal(HttpStatusCode.Created, amend1Res.StatusCode);
        var amend1 = await ReadJson(amend1Res);
        Assert.Equal(1, amend1.GetProperty("sequence").GetInt32());

        // Targeting non-latest (origId) -> 409
        var staleAmend = await PostFiling("2026-07-01", "2026-09-30", 120m,
            amendsFilingId: origId, amendmentReason: "Retry");
        Assert.Equal(HttpStatusCode.Conflict, staleAmend.StatusCode);
        Assert.Equal("tax_amendment_not_latest", (await ReadJson(staleAmend)).GetProperty("code").GetString());

        // Both come back in report.filings
        var report = await OkJson(await _host.Send(
            HttpMethod.Get, "/api/tax/report?from=2026-07-01&to=2026-09-30", null, _owner.Token, Org));
        var filings = report.GetProperty("filings").EnumerateArray().ToList();
        Assert.Equal(2, filings.Count);
        Assert.Contains(filings, f => f.GetProperty("sequence").GetInt32() == 0);
        Assert.Contains(filings, f => f.GetProperty("sequence").GetInt32() == 1);
    }

    [Fact]
    public async Task LockOnFile_AdvancesLock_NeverMovesEarlier_AndRequiresClosePermission()
    {
        // Q2 filing with lockBooks=true
        var q2 = await PostFiling("2026-04-01", "2026-06-30", 0m, reference: "Q2-REF", lockBooks: true);
        Assert.Equal(HttpStatusCode.Created, q2.StatusCode);

        WithDb(db =>
        {
            var setting = db.CompanySettings.Single();
            Assert.Equal(new DateTime(2026, 6, 30), setting.BooksLockedThrough);
            var change = db.BooksLockChanges.OrderByDescending(c => c.Id).First();
            Assert.Equal("Tax filing Apr\u2013Jun 2026 (Q2-REF)", change.Reason);
        });

        // Move lock later manually to 2026-09-30, then file Q1 (2026-01-01..2026-03-31) with lockBooks=true -> lock unchanged
        WithDb(db => db.CompanySettings.Single().BooksLockedThrough = new DateTime(2026, 9, 30));
        var q1 = await PostFiling("2026-01-01", "2026-03-31", 0m, reference: "Q1-REF", lockBooks: true);
        Assert.Equal(HttpStatusCode.Created, q1.StatusCode);
        WithDb(db => Assert.Equal(new DateTime(2026, 9, 30), db.CompanySettings.Single().BooksLockedThrough));

        // User with admin.taxes.manage WITHOUT financials.periods.close -> 403 books_lock_not_allowed, nothing stored
        var taxOnlyUser = await _host.CreateUser("tax-no-close@example.test");
        await _host.AddMember(_companyId, taxOnlyUser.Id, CompanyMemberRole.Member);
        WithDb(db =>
        {
            var role = new SecurityRole
            {
                Name = "Tax Filer Only",
                DisplayName = "Tax Filer Only"
            };
            db.SecurityRoles.Add(role);
            db.SaveChanges();

            var perms = db.SecurityPermissions
                .Where(p => p.Name == SystemPermissions.AdminTaxesManage || p.Name == SystemPermissions.ReportsView)
                .ToList();
            foreach (var p in perms)
            {
                db.SecurityRolePermissions.Add(new SecurityRolePermission
                {
                    SecurityRoleId = role.Id,
                    SecurityPermissionId = p.Id
                });
            }
            var member = db.CompanyMembers.Single(m => m.UserId == taxOnlyUser.Id);
            db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole
            {
                CompanyMemberId = member.Id,
                SecurityRoleId = role.Id
            });
        });

        var q3Denied = await PostFiling("2026-07-01", "2026-09-30", 0m, lockBooks: true, token: taxOnlyUser.Token);
        Assert.Equal(HttpStatusCode.Forbidden, q3Denied.StatusCode);
        Assert.Equal("books_lock_not_allowed", (await ReadJson(q3Denied)).GetProperty("code").GetString());
        WithDb(db => Assert.DoesNotContain(db.TaxFilings, f => f.PeriodStart == new DateTime(2026, 7, 1)));
    }

    [Fact]
    public async Task InvoicingOnlyWorkspace_AllowsFilingWithoutLock_DeniesLockBooks_AndSetsCanLockFalse()
    {
        WithDb(db => db.Companies.Single(c => c.Id == _companyId).ActiveModulesJson = "[\"sales\",\"purchases\"]");

        var report = await OkJson(await _host.Send(
            HttpMethod.Get, "/api/tax/report?from=2026-07-01&to=2026-09-30", null, _owner.Token, Org));
        Assert.True(report.GetProperty("canRecordFiling").GetBoolean());
        Assert.False(report.GetProperty("canLockOnFiling").GetBoolean());

        var denied = await PostFiling("2026-07-01", "2026-09-30", 0m, lockBooks: true);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("books_lock_not_allowed", (await ReadJson(denied)).GetProperty("code").GetString());

        var ok = await PostFiling("2026-07-01", "2026-09-30", 0m, lockBooks: false);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    [Fact]
    public async Task ChangedSinceFiling_Accrual_DetectsPostAfterReopen_VoidInNextPeriodDoesNotChangeFiledPeriod()
    {
        var inv1 = await CreatePostedInvoice(new DateTime(2026, 7, 10), (_vat12Id, 1000m, 120m));
        var fileRes = await PostFiling("2026-07-01", "2026-09-30", 120m, lockBooks: true);
        Assert.Equal(HttpStatusCode.Created, fileRes.StatusCode);

        // Reopen books with a reason and post a new invoice in Q3 -> Q3 flags changedSinceFiling
        var reopen = await _host.Send(HttpMethod.Put, "/api/Organizations/current/books-lock",
            new { lockedThrough = (DateTime?)null, reason = "Post missed Q3 invoice" }, _owner.Token, Org);
        await OkJson(reopen);

        await CreatePostedInvoice(new DateTime(2026, 8, 5), (_red5Id, 400m, 20m));

        var afterPost = await OkJson(await _host.Send(
            HttpMethod.Get, "/api/tax/report?from=2026-07-01&to=2026-09-30", null, _owner.Token, Org));
        var changed = afterPost.GetProperty("changedSinceFiling");
        Assert.True(changed.GetProperty("changed").GetBoolean());
        Assert.Equal(20m, changed.GetProperty("netDifference").GetDecimal());
        var diffRow = Assert.Single(changed.GetProperty("rows").EnumerateArray());
        Assert.Equal("output", diffRow.GetProperty("side").GetString());
        Assert.Equal("RED5", diffRow.GetProperty("taxCode").GetString());
        Assert.Equal(20m, diffRow.GetProperty("difference").GetDecimal());

        // Void inv1 in Q4 (next period) -> Q3's netDifference must remain 20m (not affected by Q4 void)
        var voidRes = await _host.Send(HttpMethod.Post,
            $"/api/Sales/VoidSalesInvoice?id={inv1}&date=2026-10-10", null, _owner.Token, Org);
        await OkJson(voidRes);

        var afterVoid = await OkJson(await _host.Send(
            HttpMethod.Get, "/api/tax/report?from=2026-07-01&to=2026-09-30", null, _owner.Token, Org));
        Assert.Equal(20m, afterVoid.GetProperty("changedSinceFiling").GetProperty("netDifference").GetDecimal());
    }

    [Fact]
    public async Task ChangedSinceFiling_CashBasis_DetectsVoidedVendorPaymentRetroactively()
    {
        WithDb(db => db.CompanySettings.Single().AccountingBasis = AccountingBasis.Cash);

        var billId = await CreatePostedBill(new DateTime(2026, 7, 5), (_vat12Id, 500m, 60m));
        var paymentId = await PayBill(billId, 560m, new DateTime(2026, 8, 12));

        var fileRes = await PostFiling("2026-07-01", "2026-09-30", -60m);
        Assert.Equal(HttpStatusCode.Created, fileRes.StatusCode);

        var voidRes = await _host.Send(HttpMethod.Post,
            $"/api/Purchasing/VoidPayment?id={paymentId}&date=2026-10-05&reason=Reversed",
            null, _owner.Token, Org);
        await OkJson(voidRes);

        var report = await OkJson(await _host.Send(
            HttpMethod.Get, "/api/tax/report?from=2026-07-01&to=2026-09-30", null, _owner.Token, Org));
        var changed = report.GetProperty("changedSinceFiling");
        Assert.True(changed.GetProperty("changed").GetBoolean());
        Assert.Equal(60m, changed.GetProperty("netDifference").GetDecimal());
    }

    [Fact]
    public async Task BasisSwitchedAfterFiling_UsesStoredBasis_NotFlaggedWhenAccrualUnchanged()
    {
        // Accrual filing for Q3 with an unpaid invoice (so Cash would be 0, Accrual is 120)
        await CreatePostedInvoice(new DateTime(2026, 7, 15), (_vat12Id, 1000m, 120m));
        var fileRes = await PostFiling("2026-07-01", "2026-09-30", 120m);
        Assert.Equal(HttpStatusCode.Created, fileRes.StatusCode);

        // Switch workspace to Cash basis
        WithDb(db => db.CompanySettings.Single().AccountingBasis = AccountingBasis.Cash);

        var report = await OkJson(await _host.Send(
            HttpMethod.Get, "/api/tax/report?from=2026-07-01&to=2026-09-30", null, _owner.Token, Org));
        Assert.Equal("Cash", report.GetProperty("basis").GetString());
        Assert.False(report.GetProperty("changedSinceFiling").GetProperty("changed").GetBoolean());
    }

    [Fact]
    public async Task RouteTable_HasNoPutPatchOrDeleteForTaxFilings()
    {
        foreach (var path in new[] { "/api/tax/filings", "/api/tax/filings/1" })
        {
            foreach (var method in new[] { HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
            {
                var res = await _host.Send(method, path, new { }, _owner.Token, Org);
                Assert.True(
                    res.StatusCode == HttpStatusCode.NotFound || res.StatusCode == HttpStatusCode.MethodNotAllowed,
                    $"Expected 404 or 405 for {method} {path}, got {res.StatusCode}");
            }
        }
    }

    [Fact]
    public async Task Access_PostRequiresAdminTaxesManage_GetAllowsReportsView()
    {
        var viewer = await _host.CreateUser("tax-filing-viewer@example.test");
        await _host.AddMember(_companyId, viewer.Id, CompanyMemberRole.Member);
        await _host.SetFunctionalRoles(_companyId, viewer.Id, "Auditor / Viewer");

        var getRes = await _host.Send(HttpMethod.Get, "/api/tax/filings", null, viewer.Token, Org);
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);

        var postRes = await PostFiling("2026-07-01", "2026-09-30", 0m, token: viewer.Token);
        Assert.Equal(HttpStatusCode.Forbidden, postRes.StatusCode);
    }
}
