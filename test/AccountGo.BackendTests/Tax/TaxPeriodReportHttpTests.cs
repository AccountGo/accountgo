// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using AccountGo.BackendTests.Auditing;
using Api.Data;
using Core.Domain;
using AccountGo.BackendTests.PublicIds;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.TaxSystem;
using Core.Tenancy;
using Dto.Banking;
using Dto.Subscription;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Services.Billing;
using Xunit;

namespace AccountGo.BackendTests.TaxSystem;

public sealed class TaxPeriodReportHttpTests : IAsyncLifetime
{
    private readonly AuditTestHost _host = new();
    private AuditTestHost.UserSession _owner = null!;
    private int _companyId;
    private const string Org = "tax-period-report";

    private int _vat12Id;
    private int _red5Id;
    private int _customerId;
    private int _vendorId;
    private int _itemId;
    private int _measurementId;
    private int _expenseAccountId;
    private int _salesTaxAccountId;
    private int _purchasingTaxAccountId;

    public async Task InitializeAsync()
    {
        await _host.InitializeAsync();
        _owner = await _host.CreateUser("tax-owner@example.test");
        _companyId = await _host.CreateWorkspace(_owner.Token, "Tax Period Co", Org);
        await _host.SetFunctionalRoles(_companyId, _owner.Id, "Accountant");

        WithDb(db =>
        {
            var setting = db.CompanySettings.Single();
            setting.BooksStartDate = new DateTime(2025, 1, 1);
            setting.AccountingBasis = AccountingBasis.Accrual;
            setting.TaxRegime = TaxRegime.VatGst;
            setting.FiscalYearPattern = FiscalYearPattern.Calendar;

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
            _salesTaxAccountId = salesTaxAcc.Id;
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
                Name = "Tax test services",
                ItemType = ItemTypes.Service,
                MeasurementId = meas.Id,
                SalesAccountId = salesAcc.Id
            };
            var item = new Item
            {
                CompanyId = _companyId,
                No = "TAX-SVC",
                Code = "TAX-SVC",
                Description = "Tax service",
                Price = 1000m,
                ItemCategory = cat,
                SmallestMeasurementId = meas.Id,
                SalesAccount = salesAcc
            };
            var customer = new Customer
            {
                CompanyId = _companyId,
                No = "TAX-CUST",
                Party = new Party { CompanyId = _companyId, Name = "Tax Customer", PartyType = PartyTypes.Customer },
                PrimaryContact = new Contact
                {
                    CompanyId = _companyId,
                    FirstName = "Tax",
                    LastName = "Customer",
                    Party = new Party { CompanyId = _companyId, Name = "Tax Contact", PartyType = PartyTypes.Contact }
                },
                AccountsReceivableAccount = arAcc,
                SalesAccount = salesAcc,
                CustomerAdvancesAccount = advAcc
            };
            var vendor = new Vendor
            {
                CompanyId = _companyId,
                No = "TAX-VEND",
                Party = new Party { CompanyId = _companyId, Name = "Tax Vendor", PartyType = PartyTypes.Vendor },
                PrimaryContact = new Contact
                {
                    CompanyId = _companyId,
                    FirstName = "Tax",
                    LastName = "Vendor",
                    Party = new Party { CompanyId = _companyId, Name = "Vendor Contact", PartyType = PartyTypes.Contact }
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

    private Task<HttpResponseMessage> GetReport(string from, string to, string? token = null, string? org = null) =>
        _host.Send(HttpMethod.Get, $"/api/tax/report?from={from}&to={to}", null, token ?? _owner.Token, org ?? Org);

    private static async Task<JsonElement> OkJson(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await AuditTestHost.Json(response);
    }

    private async Task<int> CreatePostedInvoice(
        DateTime date,
        params (int? TaxId, decimal Amount, decimal TaxAmount)[] lines)
        => await CreatePostedInvoiceWithShipping(date, 0m, lines);

    private async Task<int> CreatePostedInvoiceWithShipping(
        DateTime date,
        decimal shipping,
        params (int? TaxId, decimal Amount, decimal TaxAmount)[] lines)
    {
        var dto = new Dto.Sales.SalesInvoice
        {
            CustomerId = _customerId,
            InvoiceDate = date,
            Status = "Open",
            ShippingHandlingCharge = shipping,
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
        var res = await _host.SendDto(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", dto, _companyId, _owner.Token, Org);
        var json = await OkJson(res);
        return _host.Key<SalesInvoiceHeader>(json.GetProperty("id").GetString()!);
    }

    private async Task<int> CreatePostedBill(
        DateTime date,
        params (int? TaxId, decimal Amount, decimal TaxAmount)[] lines)
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
        var apAccId = 0;
        WithDb(db =>
        {
            apAccId = db.Accounts.Single(a => a.AccountCode == "20110").Id;
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
        return bill.Id;
    }

    [Fact]
    public async Task Report_TwoTaxCodes_NoTaxLine_AndShipping_ComputesRowsAndNet()
    {
        await CreatePostedInvoiceWithShipping(
            new DateTime(2026, 10, 10),
            50m,
            (_vat12Id, 1000m, 120m),
            (_red5Id, 200m, 10m),
            (null, 300m, 0m));

        await CreatePostedBill(
            new DateTime(2026, 10, 12),
            (_vat12Id, 500m, 60m));

        var json = await OkJson(await GetReport("2026-10-01", "2026-10-31"));

        Assert.Equal("Accrual", json.GetProperty("basis").GetString());
        Assert.Equal(1550m, json.GetProperty("output").GetProperty("taxable").GetDecimal());
        Assert.Equal(130m, json.GetProperty("output").GetProperty("tax").GetDecimal());
        Assert.Equal(500m, json.GetProperty("input").GetProperty("taxable").GetDecimal());
        Assert.Equal(60m, json.GetProperty("input").GetProperty("tax").GetDecimal());
        Assert.Equal(70m, json.GetProperty("netTax").GetDecimal());

        var outRows = json.GetProperty("output").GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(3, outRows.Count);
        Assert.Equal("RED5", outRows[0].GetProperty("taxCode").GetString());
        Assert.Equal(1, outRows[0].GetProperty("documentCount").GetInt32());
        Assert.Equal("VAT12", outRows[1].GetProperty("taxCode").GetString());
        Assert.Equal(1, outRows[1].GetProperty("documentCount").GetInt32());
        Assert.Equal("No tax", outRows[2].GetProperty("taxCode").GetString());
        Assert.Equal(350m, outRows[2].GetProperty("taxable").GetDecimal());
        Assert.Equal(JsonValueKind.Null, outRows[2].GetProperty("rate").ValueKind);
    }

    [Fact]
    public async Task Boundaries_MonthlyAndQuarterlyCycle2_InclusiveEdges()
    {
        await CreatePostedInvoice(new DateTime(2026, 7, 31), (_vat12Id, 100m, 12m));
        await CreatePostedInvoice(new DateTime(2026, 8, 1), (_vat12Id, 200m, 24m));
        await CreatePostedInvoice(new DateTime(2026, 9, 30), (_vat12Id, 300m, 36m));
        await CreatePostedInvoice(new DateTime(2026, 10, 1), (_vat12Id, 400m, 48m));
        await CreatePostedInvoice(new DateTime(2026, 10, 31), (_vat12Id, 500m, 60m));
        await CreatePostedInvoice(new DateTime(2026, 11, 1), (_vat12Id, 600m, 72m));

        // Monthly Oct 2026
        var oct = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        Assert.Equal(108m, oct.GetProperty("output").GetProperty("tax").GetDecimal());

        // Quarterly cycle 2 (Aug–Oct 2026)
        await OkJson(await _host.Send(HttpMethod.Put, "/api/tax/settings",
            new { frequency = "Quarterly", quarterCycleStartMonth = 2 }, _owner.Token, Org));
        var q = await OkJson(await GetReport("2026-08-01", "2026-10-31"));
        Assert.True(q.GetProperty("isWholePeriod").GetBoolean());
        Assert.Equal("Aug\u2013Oct 2026", q.GetProperty("periodLabel").GetString());
        Assert.Equal(168m, q.GetProperty("output").GetProperty("tax").GetDecimal());
    }

    [Fact]
    public async Task Void_InSamePeriod_NetsToZero_AndIncludesBothDocumentRows()
    {
        var invId = await CreatePostedInvoice(new DateTime(2026, 10, 5), (_vat12Id, 1000m, 120m));
        var voidRes = await _host.Send(HttpMethod.Post, $"/api/Sales/VoidSalesInvoice?id={_host.Pub<SalesInvoiceHeader>(invId)}&date=2026-10-20",
            null, _owner.Token, Org);
        Assert.True(voidRes.IsSuccessStatusCode, await voidRes.Content.ReadAsStringAsync());

        var json = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        Assert.Equal(0m, json.GetProperty("output").GetProperty("tax").GetDecimal());
        Assert.Equal(0m, json.GetProperty("netTax").GetDecimal());

        var docs = json.GetProperty("documents").EnumerateArray().ToList();
        Assert.Equal(2, docs.Count);
        Assert.Contains(docs, d => d.GetProperty("kind").GetString() == "invoice" && d.GetProperty("tax").GetDecimal() == 120m);
        Assert.Contains(docs, d => d.GetProperty("kind").GetString() == "invoiceVoid" && d.GetProperty("tax").GetDecimal() == -120m);
    }

    [Fact]
    public async Task Void_InNextPeriod_LeavesOriginalUnchanged_AndShowsNegativeInNextPeriod()
    {
        var invId = await CreatePostedInvoice(new DateTime(2026, 10, 15), (_vat12Id, 1000m, 120m));
        var voidRes = await _host.Send(HttpMethod.Post, $"/api/Sales/VoidSalesInvoice?id={_host.Pub<SalesInvoiceHeader>(invId)}&date=2026-11-05",
            null, _owner.Token, Org);
        Assert.True(voidRes.IsSuccessStatusCode, await voidRes.Content.ReadAsStringAsync());

        var oct = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        Assert.Equal(120m, oct.GetProperty("output").GetProperty("tax").GetDecimal());

        var nov = await OkJson(await GetReport("2026-11-01", "2026-11-30"));
        Assert.Equal(-120m, nov.GetProperty("output").GetProperty("tax").GetDecimal());
        Assert.Equal(-120m, nov.GetProperty("netTax").GetDecimal());
    }

    [Fact]
    public async Task DraftInvoice_IsExcluded()
    {
        var dto = new Dto.Sales.SalesInvoice
        {
            CustomerId = _customerId,
            InvoiceDate = new DateTime(2026, 10, 10),
            Status = "Draft",
            SalesInvoiceLines = new List<Dto.Sales.SalesInvoiceLine>
            {
                new Dto.Sales.SalesInvoiceLine
                {
                    ItemId = _itemId,
                    MeasurementId = _measurementId,
                    Quantity = 1m,
                    Amount = 1000m,
                    TaxId = _vat12Id,
                    TaxAmount = 120m
                }
            }
        };
        await OkJson(await _host.SendDto(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", dto, _companyId, _owner.Token, Org));

        var json = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        Assert.Equal(0m, json.GetProperty("output").GetProperty("tax").GetDecimal());
        Assert.Empty(json.GetProperty("documents").EnumerateArray());
    }

    [Fact]
    public async Task IssuedUnpostedInvoice_IsIncluded_AndListedInWarnings()
    {
        WithDb(db =>
        {
            db.SalesInvoiceHeaders.Add(new SalesInvoiceHeader
            {
                CompanyId = _companyId,
                CustomerId = _customerId,
                No = "INV-UNPOSTED",
                Date = new DateTime(2026, 10, 8),
                Status = SalesInvoiceStatus.Open,
                GeneralLedgerHeaderId = null,
                SalesInvoiceLines =
                {
                    new SalesInvoiceLine
                    {
                        CompanyId = _companyId,
                        ItemId = _itemId,
                        MeasurementId = _measurementId,
                        Quantity = 1m,
                        Amount = 1000m,
                        TaxId = _vat12Id,
                        TaxAmount = 120m
                    }
                }
            });
        });

        var json = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        Assert.Equal(120m, json.GetProperty("output").GetProperty("tax").GetDecimal());

        var unposted = json.GetProperty("warnings").GetProperty("unposted").EnumerateArray().ToList();
        Assert.Single(unposted);
        Assert.Equal("INV-UNPOSTED", unposted[0].GetProperty("no").GetString());
        Assert.Equal("invoice", unposted[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task BillWithInputTax_ReducesNet_AndCanBeNegative()
    {
        await CreatePostedInvoice(new DateTime(2026, 10, 2), (_vat12Id, 500m, 60m));
        await CreatePostedBill(new DateTime(2026, 10, 4), (_vat12Id, 1000m, 120m));

        var json = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        Assert.Equal(60m, json.GetProperty("output").GetProperty("tax").GetDecimal());
        Assert.Equal(120m, json.GetProperty("input").GetProperty("tax").GetDecimal());
        Assert.Equal(-60m, json.GetProperty("netTax").GetDecimal());
    }

    [Fact]
    public async Task BankStatementLine_RecordedAsExpenseWithTax_AppearsAsInputTax()
    {
        var bankRes = await _host.Send(HttpMethod.Post, "/api/Banking/Accounts",
            new CreateBankAccountRequest { Name = "Tax Bank", Type = 1 }, _owner.Token, Org);
        var bankJson = await OkJson(bankRes);
        var bankId = _host.Key<Bank>(bankJson.GetProperty("id").GetString()!);

        int lineId = 0;
        WithDb(db =>
        {
            var import = new BankStatementImport
            {
                CompanyId = _companyId,
                BankId = bankId,
                Source = BankStatementSources.Csv,
                FileName = "tax-test.csv",
                ImportedAt = DateTime.UtcNow,
                ImportedBy = "test"
            };
            db.Add(import);
            db.SaveChanges();
            var line = new BankStatementLine
            {
                CompanyId = _companyId,
                BankId = bankId,
                BankStatementImportId = import.Id,
                Source = BankStatementSources.Csv,
                ExternalId = Guid.NewGuid().ToString("N"),
                PostedDate = new DateTime(2026, 10, 14),
                Payee = "Host Co",
                Amount = -112m
            };
            db.BankStatementLines.Add(line);
            db.SaveChanges();
            lineId = line.Id;
        });

        var recRes = await _host.SendDto(HttpMethod.Post, $"/api/Banking/StatementLines/{_host.Pub<BankStatementLine>(lineId)}/RecordExpense",
            new RecordExpenseFromStatementLineRequest
            {
                NewVendor = new QuickVendorFromStatementRequest { Name = "Host Co" },
                ExpenseAccountId = _expenseAccountId,
                TaxId = _vat12Id
            }, _companyId, _owner.Token, Org);
        Assert.True(recRes.IsSuccessStatusCode, await recRes.Content.ReadAsStringAsync());

        var json = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        Assert.Equal(12m, json.GetProperty("input").GetProperty("tax").GetDecimal());
        Assert.Equal(100m, json.GetProperty("input").GetProperty("taxable").GetDecimal());
    }

    [Fact]
    public async Task LedgerCheck_MatchesDocumentsAndDetectsManualJournal()
    {
        await CreatePostedInvoice(new DateTime(2026, 10, 10), (_vat12Id, 1000m, 120m));
        await CreatePostedBill(new DateTime(2026, 10, 11), (_vat12Id, 500m, 60m));

        var clean = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        var accounts = clean.GetProperty("ledger").GetProperty("accounts").EnumerateArray().ToList();
        var acc20300 = accounts.Single(a => a.GetProperty("code").GetString() == "20300");
        var acc10160 = accounts.Single(a => a.GetProperty("code").GetString() == "10160");

        Assert.Equal(0m, acc20300.GetProperty("other").GetDecimal());
        Assert.Equal(0m, acc10160.GetProperty("other").GetDecimal());

        // Post manual journal crediting 20300 by 50
        WithDb(db =>
        {
            var cashAccId = db.Accounts.Single(a => a.AccountCode == "10111").Id;
            db.GeneralLedgerHeaders.Add(new GeneralLedgerHeader
            {
                CompanyId = _companyId,
                Date = new DateTime(2026, 10, 25),
                DocumentType = DocumentTypes.JournalEntry,
                Description = "Manual VAT adjustment",
                GeneralLedgerLines =
                {
                    new GeneralLedgerLine { CompanyId = _companyId, AccountId = cashAccId, DrCr = DrOrCrSide.Dr, Amount = 50m },
                    new GeneralLedgerLine { CompanyId = _companyId, AccountId = _salesTaxAccountId, DrCr = DrOrCrSide.Cr, Amount = 50m }
                }
            });
        });

        var withJournal = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        var accAfter = withJournal.GetProperty("ledger").GetProperty("accounts").EnumerateArray()
            .Single(a => a.GetProperty("code").GetString() == "20300");
        Assert.Equal(50m, accAfter.GetProperty("other").GetDecimal());
        Assert.Equal(170m, accAfter.GetProperty("movement").GetDecimal());
        Assert.Equal(120m, accAfter.GetProperty("fromDocuments").GetDecimal());
        Assert.Single(accAfter.GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task ClosedFiscalYear_DoesNotChangeTaxReportOrLedgerCheck()
    {
        await CreatePostedInvoice(new DateTime(2025, 10, 10), (_vat12Id, 1000m, 120m));
        var before = await OkJson(await GetReport("2025-10-01", "2025-10-31"));

        var previewRes = await _host.Send(HttpMethod.Get, "/api/Financials/YearEnd/2025", null, _owner.Token, Org);
        var preview = await OkJson(previewRes);
        var warnings = preview.GetProperty("warnings").GetArrayLength();
        var closeRes = await _host.Send(HttpMethod.Post, "/api/Financials/YearEnd/2025/Close",
            new
            {
                acknowledgeWarnings = warnings > 0,
                warningExceptionReason = warnings > 0 ? "Accepted for the tax report test." : null,
                previewHash = preview.GetProperty("previewHash").GetString()
            }, _owner.Token, Org);
        Assert.True(closeRes.IsSuccessStatusCode, await closeRes.Content.ReadAsStringAsync());

        var after = await OkJson(await GetReport("2025-10-01", "2025-10-31"));
        Assert.Equal(
            before.GetProperty("netTax").GetDecimal(),
            after.GetProperty("netTax").GetDecimal());
        Assert.Equal(
            before.GetProperty("ledger").GetProperty("accounts").EnumerateArray().Single(a => a.GetProperty("code").GetString() == "20300").GetProperty("movement").GetDecimal(),
            after.GetProperty("ledger").GetProperty("accounts").EnumerateArray().Single(a => a.GetProperty("code").GetString() == "20300").GetProperty("movement").GetDecimal());
    }

    [Fact]
    public async Task LockStatus_ReflectsNonePartialAndFull()
    {
        WithDb(db => db.CompanySettings.Single().BooksLockedThrough = new DateTime(2026, 9, 30));
        var none = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        Assert.Equal("none", none.GetProperty("lock").GetProperty("state").GetString());

        WithDb(db => db.CompanySettings.Single().BooksLockedThrough = new DateTime(2026, 10, 15));
        var partial = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        Assert.Equal("partial", partial.GetProperty("lock").GetProperty("state").GetString());

        WithDb(db => db.CompanySettings.Single().BooksLockedThrough = new DateTime(2026, 10, 31));
        var full = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        Assert.Equal("full", full.GetProperty("lock").GetProperty("state").GetString());
    }

    [Fact]
    public async Task InvoicingOnlyWorkspace_WorksWithNullLedgerAndLock()
    {
        await CreatePostedInvoice(new DateTime(2026, 10, 10), (_vat12Id, 1000m, 120m));
        WithDb(db => db.Companies.Single(c => c.Id == _companyId).ActiveModulesJson = "[\"sales\",\"purchases\"]");

        var json = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        Assert.Equal(120m, json.GetProperty("output").GetProperty("tax").GetDecimal());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("ledger").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("lock").ValueKind);
    }

    [Fact]
    public async Task Access_MissingModulesOrPermission_Returns403()
    {
        // Member without reports.view
        var member = await _host.CreateUser("tax-member@example.test");
        await _host.AddMember(_companyId, member.Id, CompanyMemberRole.Member);
        await _host.SetFunctionalRoles(_companyId, member.Id, "Sales Representative");

        var deniedPerm = await GetReport("2026-10-01", "2026-10-31", token: member.Token);
        Assert.Equal(HttpStatusCode.Forbidden, deniedPerm.StatusCode);

        var deniedPeriods = await _host.Send(HttpMethod.Get, "/api/tax/periods", null, member.Token, Org);
        Assert.Equal(HttpStatusCode.Forbidden, deniedPeriods.StatusCode);

        var op = global::Core.Authorization.AccessOperationCatalog.GetPolicy(
            global::Core.Authorization.AccessOperationCatalog.TaxReportsView)!;
        Assert.Contains("sales", op.RequiredModulesAny);
        Assert.Contains("purchases", op.RequiredModulesAny);
        Assert.Contains("financials", op.RequiredModulesAny);
        Assert.Contains("reports", op.RequiredModulesAny);
    }

    [Fact]
    public async Task Validation_InvalidRangeAndOver400Days_Returns400()
    {
        var reversed = await GetReport("2026-10-31", "2026-10-01");
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);

        var tooLong = await GetReport("2025-01-01", "2026-06-01");
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task CashBasisWorkspace_Returns200WithCashBasis()
    {
        WithDb(db => db.CompanySettings.Single().AccountingBasis = AccountingBasis.Cash);

        var json = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        Assert.Equal("Cash", json.GetProperty("basis").GetString());
    }

    [Fact]
    public async Task Settings_GetDefault_UpdateCycle_EnforcePermissionsAndValidation()
    {
        // Default derived from Calendar FY = 1
        var initial = await OkJson(await _host.Send(HttpMethod.Get, "/api/tax/settings", null, _owner.Token, Org));
        Assert.Equal(1, initial.GetProperty("defaultQuarterCycleStartMonth").GetInt32());
        Assert.Equal(1, initial.GetProperty("effectiveQuarterCycleStartMonth").GetInt32());

        // PUT cycle 2
        var updated = await OkJson(await _host.Send(HttpMethod.Put, "/api/tax/settings",
            new { frequency = "Quarterly", quarterCycleStartMonth = 2 }, _owner.Token, Org));
        Assert.Equal(2, updated.GetProperty("quarterCycleStartMonth").GetInt32());

        var periods = await OkJson(await _host.Send(HttpMethod.Get, "/api/tax/periods?count=4", null, _owner.Token, Org));
        var labels = periods.EnumerateArray().Select(p => p.GetProperty("label").GetString() ?? "").ToList();
        Assert.Contains(labels, l => l.StartsWith("Aug\u2013Oct", StringComparison.Ordinal) || l.StartsWith("Nov ", StringComparison.Ordinal));

        // Bad values -> 400
        var badMonth = await _host.Send(HttpMethod.Put, "/api/tax/settings",
            new { frequency = "Quarterly", quarterCycleStartMonth = 4 }, _owner.Token, Org);
        Assert.Equal(HttpStatusCode.BadRequest, badMonth.StatusCode);

        var badFreq = await _host.Send(HttpMethod.Put, "/api/tax/settings",
            new { frequency = "Yearly", quarterCycleStartMonth = 1 }, _owner.Token, Org);
        Assert.Equal(HttpStatusCode.BadRequest, badFreq.StatusCode);

        // Without admin.taxes.manage -> 403
        var viewer = await _host.CreateUser("tax-viewer@example.test");
        await _host.AddMember(_companyId, viewer.Id, CompanyMemberRole.Member);
        await _host.SetFunctionalRoles(_companyId, viewer.Id, "Auditor / Viewer");
        var forbidden = await _host.Send(HttpMethod.Put, "/api/tax/settings",
            new { frequency = "Monthly", quarterCycleStartMonth = (int?)null }, viewer.Token, Org);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task ArchivedTax_StillAppearsWithStoredAmounts()
    {
        await CreatePostedInvoice(new DateTime(2026, 10, 9), (_red5Id, 400m, 20m));
        WithDb(db => db.Taxes.Single(t => t.Id == _red5Id).IsActive = false);

        var json = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        var row = Assert.Single(json.GetProperty("output").GetProperty("rows").EnumerateArray());
        Assert.Equal("RED5", row.GetProperty("taxCode").GetString());
        Assert.Equal(400m, row.GetProperty("taxable").GetDecimal());
        Assert.Equal(20m, row.GetProperty("tax").GetDecimal());
    }

    [Fact]
    public async Task UnpostedVoidedDocuments_AreExcludedWithoutWarnings()
    {
        WithDb(db =>
        {
            var inv = new SalesInvoiceHeader
            {
                CompanyId = _companyId,
                CustomerId = _customerId,
                No = "INV-UNPOSTED-VOID",
                Date = new DateTime(2026, 10, 12),
                Status = SalesInvoiceStatus.Void,
                GeneralLedgerHeaderId = null,
                VoidGeneralLedgerHeaderId = null,
                SalesInvoiceLines = new List<SalesInvoiceLine>
                {
                    new()
                    {
                        CompanyId = _companyId,
                        ItemId = _itemId,
                        MeasurementId = _measurementId,
                        TaxId = _vat12Id,
                        Quantity = 1m,
                        Amount = 1000m,
                        TaxAmount = 120m
                    }
                }
            };
            var bill = new PurchaseInvoiceHeader
            {
                CompanyId = _companyId,
                VendorId = _vendorId,
                No = "BILL-UNPOSTED-VOID",
                VendorInvoiceNo = "V-UNPOSTED-VOID",
                Date = new DateTime(2026, 10, 13),
                Status = PurchaseInvoiceStatus.Void,
                GeneralLedgerHeaderId = null,
                VoidGeneralLedgerHeaderId = null,
                PurchaseInvoiceLines = new List<PurchaseInvoiceLine>
                {
                    new()
                    {
                        CompanyId = _companyId,
                        ItemId = _itemId,
                        AccountId = _expenseAccountId,
                        TaxId = _vat12Id,
                        Quantity = 1m,
                        Amount = 500m,
                        TaxAmount = 60m
                    }
                }
            };
            db.SalesInvoiceHeaders.Add(inv);
            db.PurchaseInvoiceHeaders.Add(bill);
        });

        var json = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        Assert.Equal(0m, json.GetProperty("output").GetProperty("tax").GetDecimal());
        Assert.Equal(0m, json.GetProperty("input").GetProperty("tax").GetDecimal());
        Assert.Empty(json.GetProperty("documents").EnumerateArray());
        Assert.Empty(json.GetProperty("warnings").GetProperty("unposted").EnumerateArray());
    }

    [Fact]
    public async Task PostedDocumentsWithNullStatus_AreIncluded()
    {
        WithDb(db =>
        {
            var glInv = new GeneralLedgerHeader
            {
                CompanyId = _companyId,
                Date = new DateTime(2026, 10, 12),
                DocumentType = DocumentTypes.SalesInvoice,
                Description = "Legacy posted invoice"
            };
            var glBill = new GeneralLedgerHeader
            {
                CompanyId = _companyId,
                Date = new DateTime(2026, 10, 13),
                DocumentType = DocumentTypes.PurchaseInvoice,
                Description = "Legacy posted bill"
            };
            db.GeneralLedgerHeaders.AddRange(glInv, glBill);
            db.SaveChanges();

            db.SalesInvoiceHeaders.Add(new SalesInvoiceHeader
            {
                CompanyId = _companyId,
                CustomerId = _customerId,
                No = "INV-NULL-STATUS",
                Date = new DateTime(2026, 10, 12),
                Status = null,
                GeneralLedgerHeaderId = glInv.Id,
                SalesInvoiceLines = new List<SalesInvoiceLine>
                {
                    new()
                    {
                        CompanyId = _companyId,
                        ItemId = _itemId,
                        MeasurementId = _measurementId,
                        TaxId = _vat12Id,
                        Quantity = 1m,
                        Amount = 1000m,
                        TaxAmount = 120m
                    }
                }
            });

            db.PurchaseInvoiceHeaders.Add(new PurchaseInvoiceHeader
            {
                CompanyId = _companyId,
                VendorId = _vendorId,
                No = "BILL-NULL-STATUS",
                VendorInvoiceNo = "V-NULL-STATUS",
                Date = new DateTime(2026, 10, 13),
                Status = null,
                GeneralLedgerHeaderId = glBill.Id,
                PurchaseInvoiceLines = new List<PurchaseInvoiceLine>
                {
                    new()
                    {
                        CompanyId = _companyId,
                        ItemId = _itemId,
                        AccountId = _expenseAccountId,
                        TaxId = _vat12Id,
                        Quantity = 1m,
                        Amount = 500m,
                        TaxAmount = 60m
                    }
                }
            });
        });

        var json = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        Assert.Equal(120m, json.GetProperty("output").GetProperty("tax").GetDecimal());
        Assert.Equal(60m, json.GetProperty("input").GetProperty("tax").GetDecimal());
        Assert.Equal(2, json.GetProperty("documents").GetArrayLength());
    }

    [Fact]
    public async Task OrganizationsCurrent_UsesTaxSubquery_RespectsTenantFilter()
    {
        // Primary workspace (_companyId) has VAT12 (12% tax) -> usesTax = true.
        var primaryCurrent = await OkJson(await _host.Send(HttpMethod.Get, "/api/organizations/current", null, _owner.Token, Org));
        Assert.True(primaryCurrent.GetProperty("usesTax").GetBoolean());

        // Second workspace has TaxRegime.None and no taxes -> usesTax must stay false (tenant filter applies inside subquery).
        const string noTaxOrg = "tax-period-no-tax";
        var secondCompanyId = await _host.CreateWorkspace(_owner.Token, "No Tax Co", noTaxOrg);
        using (var scope = _host.Factory.Services.CreateScope())
        using (var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(secondCompanyId, noTaxOrg, CompanyMemberRole.Owner))
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.CompanySettings.Single().TaxRegime = TaxRegime.None;
            db.SaveChanges();
        }

        var secondaryCurrent = await OkJson(await _host.Send(HttpMethod.Get, "/api/organizations/current", null, _owner.Token, noTaxOrg));
        Assert.False(secondaryCurrent.GetProperty("usesTax").GetBoolean());
    }

    [Fact]
    public async Task Access_MissingModules_Returns403()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"accountgo_taxmod_{Guid.NewGuid():N}.db");
        try
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("DatabaseProvider", "Sqlite");
                builder.UseSetting("ConnectionStrings:SqliteConnection", $"Data Source={dbPath}");
                builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IPlanCatalog>();
                    services.AddSingleton<IPlanCatalog, InventoryOnlyPlanCatalog>();
                });
            });

            var client = factory.CreateClient();
            using var scope = factory.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var userManager = sp.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = "tax-nomod@example.test",
                Email = "tax-nomod@example.test",
                EmailConfirmed = true
            };
            Assert.True((await userManager.CreateAsync(user)).Succeeded);

            var security = sp.GetRequiredService<Services.Security.ISecurityService>();
            security.EnsureDefaultPermissionsAndRoles();
            security.AddUser(user.Email, user.Email, "No", "Mod", new List<int>());
            var token = (await sp.GetRequiredService<Api.Service.IAuthenticationService>().CreateToken(user, true)).AccessToken;

            using var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/organizations")
            {
                Content = new StringContent("{\"name\":\"Inventory Only Co\",\"slug\":\"inv-only\"}", System.Text.Encoding.UTF8, "application/json")
            };
            createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var createRes = await client.SendAsync(createReq);
            Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
            var companyId = PublicIdTestHelper.ResolveCompanyId(factory.Services, (await AuditTestHost.Json(createRes)).GetProperty("id").GetString()!);

            var tenant = sp.GetRequiredService<ITenantContext>();
            using (var imp = tenant.Impersonate(companyId, role: CompanyMemberRole.Owner))
            {
                var db = sp.GetRequiredService<ApiDbContext>();
                var member = db.CompanyMembers.Single(m => m.CompanyId == companyId && m.UserId == user.Id);
                db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole
                {
                    CompanyMemberId = member.Id,
                    SecurityRoleId = db.SecurityRoles.Single(r => r.Name == "Accountant").Id
                });
                db.SaveChanges();
            }

            using var reportReq = new HttpRequestMessage(HttpMethod.Get, "/api/tax/report?from=2026-10-01&to=2026-10-31");
            reportReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            reportReq.Headers.Add("X-AccountGo-Org", "inv-only");
            var reportRes = await client.SendAsync(reportReq);
            Assert.Equal(HttpStatusCode.Forbidden, reportRes.StatusCode);

            using var periodsReq = new HttpRequestMessage(HttpMethod.Get, "/api/tax/periods");
            periodsReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            periodsReq.Headers.Add("X-AccountGo-Org", "inv-only");
            var periodsRes = await client.SendAsync(periodsReq);
            Assert.Equal(HttpStatusCode.Forbidden, periodsRes.StatusCode);
        }
        finally
        {
            if (System.IO.File.Exists(dbPath))
                System.IO.File.Delete(dbPath);
        }
    }

    private async Task RecordInvoicePayment(int invoiceId, decimal amount, DateTime date)
    {
        var res = await _host.Send(HttpMethod.Post, "/api/Sales/RecordPayment",
            new { invoiceId = _host.Pub<SalesInvoiceHeader>(invoiceId), amount, paymentDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
            _owner.Token, Org);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
    }

    private async Task<int> PayBill(int billId, decimal amount, DateTime date)
    {
        var res = await _host.Send(HttpMethod.Post, "/api/Purchasing/PayBill",
            new { billId = _host.Pub<PurchaseInvoiceHeader>(billId), amount, date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
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

    private async Task<int> CreatePostedBillWithAccounts(
        DateTime date,
        params (int AccountId, int? TaxId, decimal Amount, decimal TaxAmount)[] lines)
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
                AccountId = l.AccountId,
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
            foreach (var (line, idx) in bill.PurchaseInvoiceLines.Select((x, i) => (x, i)))
            {
                totalSub += line.Subtotal;
                totalTax += line.TaxAmount;
                gl.GeneralLedgerLines.Add(new GeneralLedgerLine
                {
                    CompanyId = _companyId,
                    AccountId = lines[idx].AccountId,
                    DrCr = DrOrCrSide.Dr,
                    Amount = line.Subtotal
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
        return bill.Id;
    }

    private async Task<decimal> GetCashTbMovement(string from, string to, string accountCode, bool creditMinusDebit)
    {
        var arr = (await OkJson(await _host.Send(
            HttpMethod.Get,
            $"/api/Financials/trialbalance?from={from}T00:00:00&to={to}T23:59:59&basis=Cash",
            null, _owner.Token, Org))).EnumerateArray().ToList();
        var row = arr.FirstOrDefault(r => r.GetProperty("accountCode").GetString() == accountCode);
        if (row.ValueKind == JsonValueKind.Undefined)
            return 0m;
        var dr = row.GetProperty("debit").GetDecimal();
        var cr = row.GetProperty("credit").GetDecimal();
        return creditMinusDebit ? cr - dr : dr - cr;
    }

    private decimal LedgerMovement(JsonElement reportJson, int accountId)
    {
        foreach (var acc in reportJson.GetProperty("ledger").GetProperty("accounts").EnumerateArray())
        {
            if (acc.GetProperty("accountId").GetString() == _host.Pub<Core.Domain.Financials.Account>(accountId))
                return acc.GetProperty("movement").GetDecimal();
        }
        return 0m;
    }

    [Fact]
    public async Task Cash_TwoCodesPartialPaymentsAcrossMonths_MatchesTrialBalanceToTheCent()
    {
        WithDb(db => db.CompanySettings.Single().AccountingBasis = AccountingBasis.Cash);

        // 1,000 + 120 VAT12 + 1,000 + 50 RED5 = 2,170 total
        var invId = await CreatePostedInvoice(
            new DateTime(2026, 10, 5),
            (_vat12Id, 1000m, 120m),
            (_red5Id, 1000m, 50m));

        // 40% in Oct = 868.00; 60% in Nov = 1,302.00
        await RecordInvoicePayment(invId, 868m, new DateTime(2026, 10, 15));
        await RecordInvoicePayment(invId, 1302m, new DateTime(2026, 11, 10));

        var oct = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        var nov = await OkJson(await GetReport("2026-11-01", "2026-11-30"));

        Assert.Equal("Cash", oct.GetProperty("basis").GetString());
        Assert.Equal("Cash", nov.GetProperty("basis").GetString());

        var octVat = oct.GetProperty("output").GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("taxCode").GetString() == "VAT12").GetProperty("tax").GetDecimal();
        var octRed = oct.GetProperty("output").GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("taxCode").GetString() == "RED5").GetProperty("tax").GetDecimal();
        var novVat = nov.GetProperty("output").GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("taxCode").GetString() == "VAT12").GetProperty("tax").GetDecimal();
        var novRed = nov.GetProperty("output").GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("taxCode").GetString() == "RED5").GetProperty("tax").GetDecimal();

        Assert.Equal(120m, octVat + novVat);
        Assert.Equal(50m, octRed + novRed);

        var octTb = await GetCashTbMovement("2026-10-01", "2026-10-31", "20300", creditMinusDebit: true);
        var novTb = await GetCashTbMovement("2026-11-01", "2026-11-30", "20300", creditMinusDebit: true);

        Assert.Equal(octTb, oct.GetProperty("output").GetProperty("tax").GetDecimal());
        Assert.Equal(novTb, nov.GetProperty("output").GetProperty("tax").GetDecimal());

        var octLedger20300 = oct.GetProperty("ledger").GetProperty("accounts").EnumerateArray().Single(a => a.GetProperty("accountId").GetString() == _host.Pub<Core.Domain.Financials.Account>(_salesTaxAccountId));
        var novLedger20300 = nov.GetProperty("ledger").GetProperty("accounts").EnumerateArray().Single(a => a.GetProperty("accountId").GetString() == _host.Pub<Core.Domain.Financials.Account>(_salesTaxAccountId));
        Assert.Equal(0m, octLedger20300.GetProperty("other").GetDecimal());
        Assert.Equal(0m, novLedger20300.GetProperty("other").GetDecimal());
    }

    [Fact]
    public async Task Cash_UnpaidPostedInvoice_NotInFiguresAndNotInWarnings()
    {
        WithDb(db => db.CompanySettings.Single().AccountingBasis = AccountingBasis.Cash);

        await CreatePostedInvoice(new DateTime(2026, 10, 8), (_vat12Id, 1000m, 120m));

        var json = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        Assert.Equal(0m, json.GetProperty("output").GetProperty("tax").GetDecimal());
        Assert.Equal(0, json.GetProperty("documents").GetArrayLength());
        Assert.Equal(0, json.GetProperty("warnings").GetProperty("unposted").GetArrayLength());
    }

    [Fact]
    public async Task Cash_UnpostedIssuedInvoice_InWarningsAndNotInFigures()
    {
        WithDb(db =>
        {
            db.CompanySettings.Single().AccountingBasis = AccountingBasis.Cash;
            db.SalesInvoiceHeaders.Add(new SalesInvoiceHeader
            {
                CompanyId = _companyId,
                CustomerId = _customerId,
                No = "INV-UNPOSTED-CASH",
                Date = new DateTime(2026, 10, 9),
                Status = SalesInvoiceStatus.Open,
                GeneralLedgerHeaderId = null,
                SalesInvoiceLines = new List<SalesInvoiceLine>
                {
                    new()
                    {
                        CompanyId = _companyId,
                        ItemId = _itemId,
                        MeasurementId = _measurementId,
                        TaxId = _vat12Id,
                        Quantity = 1m,
                        Amount = 1000m,
                        TaxAmount = 120m
                    }
                }
            });
        });

        var json = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        Assert.Equal(0m, json.GetProperty("output").GetProperty("tax").GetDecimal());
        Assert.Single(json.GetProperty("warnings").GetProperty("unposted").EnumerateArray());
    }

    [Fact]
    public async Task Cash_BillWithCapitalizedAsset_PaidInTwoPeriods_MatchesTrialBalance10160()
    {
        int assetAccountId = 0;
        WithDb(db =>
        {
            db.CompanySettings.Single().AccountingBasis = AccountingBasis.Cash;
            assetAccountId = db.Accounts.First(a =>
                a.AccountClassId == (int)AccountClasses.Assets
                && a.AccountCode != "10160"
                && a.AccountCode != "10120"
                && !a.IsCash
                && a.ParentAccountId != null
                && !db.Accounts.Any(c => c.ParentAccountId == a.Id && c.CompanyId == _companyId)).Id;
        });

        // Capitalized asset line (500, no tax) + expense line (500 + 60 VAT12) = 1,060 total
        var billId = await CreatePostedBillWithAccounts(
            new DateTime(2026, 10, 4),
            (assetAccountId, null, 500m, 0m),
            (_expenseAccountId, _vat12Id, 500m, 60m));

        // Pay 530 in Oct, 530 in Nov
        await PayBill(billId, 530m, new DateTime(2026, 10, 20));
        await PayBill(billId, 530m, new DateTime(2026, 11, 15));

        var oct = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        var nov = await OkJson(await GetReport("2026-11-01", "2026-11-30"));

        var octTb = await GetCashTbMovement("2026-10-01", "2026-10-31", "10160", creditMinusDebit: false);
        var novTb = await GetCashTbMovement("2026-11-01", "2026-11-30", "10160", creditMinusDebit: false);

        Assert.Equal(octTb, oct.GetProperty("input").GetProperty("tax").GetDecimal());
        Assert.Equal(novTb, nov.GetProperty("input").GetProperty("tax").GetDecimal());
        Assert.Equal(60m, octTb + novTb);
    }

    [Fact]
    public async Task Cash_ManualJournalToSalesTaxAccount_AppearsInLedgerOther()
    {
        WithDb(db =>
        {
            db.CompanySettings.Single().AccountingBasis = AccountingBasis.Cash;
            var cashAcc = db.Accounts.First(a => a.AccountCode == "10110" || a.IsCash);
            var gl = new GeneralLedgerHeader
            {
                CompanyId = _companyId,
                Date = new DateTime(2026, 10, 18),
                DocumentType = DocumentTypes.JournalEntry,
                Description = "Manual VAT adjustment"
            };
            gl.GeneralLedgerLines.Add(new GeneralLedgerLine
            {
                CompanyId = _companyId,
                AccountId = cashAcc.Id,
                DrCr = DrOrCrSide.Dr,
                Amount = 75m
            });
            gl.GeneralLedgerLines.Add(new GeneralLedgerLine
            {
                CompanyId = _companyId,
                AccountId = _salesTaxAccountId,
                DrCr = DrOrCrSide.Cr,
                Amount = 75m
            });
            db.GeneralLedgerHeaders.Add(gl);
        });

        var json = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        var acc20300 = json.GetProperty("ledger").GetProperty("accounts").EnumerateArray()
            .Single(a => a.GetProperty("accountId").GetString() == _host.Pub<Core.Domain.Financials.Account>(_salesTaxAccountId));
        Assert.Equal(75m, acc20300.GetProperty("other").GetDecimal());
        var entries = acc20300.GetProperty("entries").EnumerateArray().ToList();
        Assert.Single(entries);
        Assert.Equal("Manual VAT adjustment", entries[0].GetProperty("description").GetString());
    }

    [Fact]
    public async Task Cash_BankStatementLineRecordedAsIncomeWithTax_RecognizedOnStatementDate()
    {
        int statementLineId = 0;
        int salesAccId = 0;
        WithDb(db =>
        {
            db.CompanySettings.Single().AccountingBasis = AccountingBasis.Cash;
            salesAccId = db.Accounts.Single(a => a.AccountCode == "40100").Id;
            var parentCash = db.Accounts.Single(a => a.AccountCode == "10110");
            var bankGlAcc = db.Accounts.FirstOrDefault(a =>
                a.ParentAccountId == parentCash.Id
                && !db.Accounts.Any(c => c.ParentAccountId == a.Id && c.CompanyId == _companyId));
            if (bankGlAcc == null)
            {
                bankGlAcc = new Account
                {
                    CompanyId = _companyId,
                    AccountCode = "10119",
                    AccountName = "Tax Cash Checking",
                    AccountClassId = (int)AccountClasses.Assets,
                    ParentAccountId = parentCash.Id,
                    IsCash = true,
                    DrOrCrSide = DrOrCrSide.Dr
                };
                db.Accounts.Add(bankGlAcc);
                db.SaveChanges();
            }
            var bank = new Bank
            {
                CompanyId = _companyId,
                Name = "Tax Cash Bank",
                BankName = "Test Bank",
                Number = "123456",
                AccountId = bankGlAcc.Id,
                IsDefaultForReceipts = true,
                IsDefaultForPayments = true
            };
            db.Banks.Add(bank);
            db.SaveChanges();

            var import = new BankStatementImport
            {
                CompanyId = _companyId,
                BankId = bank.Id,
                Source = BankStatementSources.Csv,
                FileName = "oct.csv",
                ImportedAt = DateTime.UtcNow,
                ImportedBy = "tax-owner@example.test"
            };
            db.BankStatementImports.Add(import);
            db.SaveChanges();

            var line = new BankStatementLine
            {
                CompanyId = _companyId,
                BankId = bank.Id,
                BankStatementImportId = import.Id,
                Source = BankStatementSources.Csv,
                PostedDate = new DateTime(2026, 10, 22),
                Memo = "Direct customer deposit",
                Amount = 1120m,
                CreatedAt = DateTime.UtcNow
            };
            db.BankStatementLines.Add(line);
            db.SaveChanges();
            statementLineId = line.Id;
        });

        var res = await _host.SendDto(
            HttpMethod.Post,
            $"/api/Banking/StatementLines/{_host.Pub<BankStatementLine>(statementLineId)}/RecordOtherIncome",
            new Dto.Banking.RecordOtherIncomeFromStatementLineRequest
            {
                CustomerId = _customerId,
                IncomeAccountId = salesAccId,
                TaxId = _vat12Id,
                Description = "Direct income"
            },
            _companyId,
            _owner.Token,
            Org);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());

        var json = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        Assert.Equal(120m, json.GetProperty("output").GetProperty("tax").GetDecimal());
        var tb = await GetCashTbMovement("2026-10-01", "2026-10-31", "20300", creditMinusDebit: true);
        Assert.Equal(tb, json.GetProperty("output").GetProperty("tax").GetDecimal());
    }

    [Fact]
    public async Task Cash_VoidedInvoice_PaidInvoiceVoidIsBlocked_MatchesTb()
    {
        WithDb(db => db.CompanySettings.Single().AccountingBasis = AccountingBasis.Cash);

        var invId = await CreatePostedInvoice(new DateTime(2026, 10, 5), (_vat12Id, 1000m, 120m));
        await RecordInvoicePayment(invId, 1120m, new DateTime(2026, 10, 12));

        // Voiding a paid invoice is blocked
        var voidPaidRes = await _host.Send(
            HttpMethod.Post,
            $"/api/Sales/VoidSalesInvoice?id={_host.Pub<SalesInvoiceHeader>(invId)}&date=2026-10-20",
            null, _owner.Token, Org);
        Assert.False(voidPaidRes.IsSuccessStatusCode);

        var json = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        var tb = await GetCashTbMovement("2026-10-01", "2026-10-31", "20300", creditMinusDebit: true);
        Assert.Equal(120m, json.GetProperty("output").GetProperty("tax").GetDecimal());
        Assert.Equal(tb, LedgerMovement(json, _salesTaxAccountId));
    }

    [Fact]
    public async Task Cash_VoidedVendorPayment_BillTaxLeavesPeriodAndMatchesTb()
    {
        WithDb(db => db.CompanySettings.Single().AccountingBasis = AccountingBasis.Cash);

        var billId = await CreatePostedBill(new DateTime(2026, 10, 5), (_vat12Id, 500m, 60m));
        var paymentId = await PayBill(billId, 560m, new DateTime(2026, 10, 15));

        var beforeVoid = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        Assert.Equal(60m, beforeVoid.GetProperty("input").GetProperty("tax").GetDecimal());

        var voidRes = await _host.Send(
            HttpMethod.Post,
            $"/api/Purchasing/VoidPayment?id={_host.Pub<VendorPayment>(paymentId)}&date=2026-10-20&reason=Entered+in+error",
            null, _owner.Token, Org);
        Assert.True(voidRes.IsSuccessStatusCode, await voidRes.Content.ReadAsStringAsync());

        var afterVoid = await OkJson(await GetReport("2026-10-01", "2026-10-31"));
        var tb = await GetCashTbMovement("2026-10-01", "2026-10-31", "10160", creditMinusDebit: false);
        Assert.Equal(0m, afterVoid.GetProperty("input").GetProperty("tax").GetDecimal());
        Assert.Equal(tb, afterVoid.GetProperty("input").GetProperty("tax").GetDecimal());
    }

    [Fact]
    public async Task Cash_Cfo4GoldenTest_ThreeInvoicesTwoBillsMixedPartialPayments_MatchesTbAndReconciles()
    {
        WithDb(db => db.CompanySettings.Single().AccountingBasis = AccountingBasis.Cash);

        // 3 invoices with mixed partial payments across Oct & Nov, two codes sharing 20300
        var inv1 = await CreatePostedInvoice(new DateTime(2026, 10, 2), (_vat12Id, 1000m, 120m), (_red5Id, 500m, 25m)); // 1645
        var inv2 = await CreatePostedInvoice(new DateTime(2026, 10, 10), (_vat12Id, 333.33m, 40m)); // 373.33
        var inv3 = await CreatePostedInvoice(new DateTime(2026, 11, 3), (_red5Id, 777m, 38.85m)); // 815.85

        await RecordInvoicePayment(inv1, 500m, new DateTime(2026, 10, 12));
        await RecordInvoicePayment(inv1, 1145m, new DateTime(2026, 11, 12));
        await RecordInvoicePayment(inv2, 186.67m, new DateTime(2026, 10, 25));
        await RecordInvoicePayment(inv2, 186.66m, new DateTime(2026, 11, 20));
        await RecordInvoicePayment(inv3, 815.85m, new DateTime(2026, 11, 25));

        // 2 bills with mixed partial payments across Oct & Nov
        var bill1 = await CreatePostedBill(new DateTime(2026, 10, 4), (_vat12Id, 400m, 48m), (_red5Id, 200m, 10m)); // 658
        var bill2 = await CreatePostedBill(new DateTime(2026, 10, 18), (_vat12Id, 250m, 30m)); // 280

        await PayBill(bill1, 258m, new DateTime(2026, 10, 22));
        await PayBill(bill1, 400m, new DateTime(2026, 11, 14));
        await PayBill(bill2, 280m, new DateTime(2026, 11, 18));

        foreach (var (from, to) in new[] { ("2026-10-01", "2026-10-31"), ("2026-11-01", "2026-11-30") })
        {
            var report = await OkJson(await GetReport(from, to));
            var tb20300 = await GetCashTbMovement(from, to, "20300", creditMinusDebit: true);
            var tb10160 = await GetCashTbMovement(from, to, "10160", creditMinusDebit: false);

            var acc20300 = report.GetProperty("ledger").GetProperty("accounts").EnumerateArray()
                .Single(a => a.GetProperty("accountId").GetString() == _host.Pub<Core.Domain.Financials.Account>(_salesTaxAccountId));
            var acc10160 = report.GetProperty("ledger").GetProperty("accounts").EnumerateArray()
                .Single(a => a.GetProperty("accountId").GetString() == _host.Pub<Core.Domain.Financials.Account>(_purchasingTaxAccountId));

            Assert.Equal(tb20300, acc20300.GetProperty("movement").GetDecimal());
            Assert.Equal(tb10160, acc10160.GetProperty("movement").GetDecimal());

            Assert.Equal(
                acc20300.GetProperty("movement").GetDecimal(),
                acc20300.GetProperty("fromDocuments").GetDecimal() + acc20300.GetProperty("other").GetDecimal());
            Assert.Equal(
                acc10160.GetProperty("movement").GetDecimal(),
                acc10160.GetProperty("fromDocuments").GetDecimal() + acc10160.GetProperty("other").GetDecimal());
        }
    }

    private sealed class InventoryOnlyPlanCatalog : IPlanCatalog
    {
        private static readonly PlanDefinitionDto InventoryOnlyPlan = new()
        {
            Key = "inventory-only",
            Name = "Inventory Only",
            Description = "Test plan without sales, purchases, financials, or reports.",
            IncludedModules = new List<string> { "inventory" },
            RequiredModules = new List<string>(),
            SelfService = false,
            MonthlyPrice = null
        };

        public string DefaultPlanKey => InventoryOnlyPlan.Key;
        public IReadOnlyList<PlanDefinitionDto> GetPlans() => new[] { InventoryOnlyPlan };
        public bool TryResolvePlan(string? key, out PlanDefinitionDto plan)
        {
            plan = InventoryOnlyPlan;
            return true;
        }
        public PlanDefinitionDto ResolveStoredPlan(string? key) => InventoryOnlyPlan;
    }
}
