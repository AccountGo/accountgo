// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.TaxSystem;
using Core.Entitlements;
using Core.Tenancy;
using Dto.Banking;
using Moq;
using Services.Banking;
using Services.Financial;
using Services.Inventory;
using Services.Purchasing;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class StatementLineExpenseServiceTests
{
    [Fact]
    public void ListExpenseCategories_maps_curated_by_code_and_puts_rest_in_More()
    {
        using var harness = new BankingTestHarness();
        harness.SeedCashTree();
        SeedExpenseTree(harness);

        var service = Build(harness, new Mock<IPurchasingService>(MockBehavior.Strict));
        var dto = service.ListExpenseCategories();

        Assert.Contains(dto.Curated, c => c.Name == "Travel" && c.Curated);
        Assert.Contains(dto.Curated, c => c.Name == "Equipment (asset)" && c.Curated);
        Assert.Contains(dto.Curated, c => c.Name == "Bank fees" && c.Curated);
        Assert.DoesNotContain(dto.Curated, c => c.Name.Contains("50118"));
        Assert.Contains(dto.More, m => m.Name == "Telephone Expenses" && !m.Curated);
        Assert.DoesNotContain(dto.More, m => m.AccountId == harness.RequireAccount("50118").Id);
    }

    [Fact]
    public void RecordExpense_saves_approves_pays_and_matches_bank_credit_without_metering()
    {
        using var harness = new BankingTestHarness();
        harness.SeedCashTree();
        SeedExpenseTree(harness);
        var bank = harness.CreateBank("Checking");
        var line = ImportPayment(harness, bank.Id, -49.99m, "Figma", BankingTestHarness.Utc(2026, 3, 15));
        var expenseAccountId = harness.RequireAccount("50115").Id;

        var purchasing = new Mock<IPurchasingService>(MockBehavior.Strict);
        PurchaseInvoiceHeader? savedBill = null;
        purchasing
            .Setup(p => p.SaveBill(It.IsAny<PurchaseInvoiceHeader>(), It.IsAny<bool>()))
            .Callback<PurchaseInvoiceHeader, bool>((b, _) =>
            {
                savedBill = b;
                b.Id = 88;
                b.No = "B-88";
                b.CompanyId = 1;
                harness.Repos.PurchaseInvoices.Insert(b);
            });
        purchasing.Setup(p => p.ApproveBill(88, "tester", It.IsAny<DateTime>()));
        Vendor? addedVendor = null;
        purchasing
            .Setup(p => p.AddVendor(It.IsAny<Vendor>()))
            .Callback<Vendor>(v =>
            {
                addedVendor = v;
                v.Id = 7;
                v.CompanyId = 1;
                harness.Repos.Vendors.Insert(v);
            });

        var headerId = harness.Repos.NextGlHeaderId();
        var bankCr = new GeneralLedgerLine
        {
            Id = harness.Repos.NextGlLineId(),
            CompanyId = 1,
            GeneralLedgerHeaderId = headerId,
            AccountId = bank.AccountId!.Value,
            DrCr = DrOrCrSide.Cr,
            Amount = 49.99m
        };
        harness.Repos.GlLines.Insert(bankCr);
        harness.Repos.GlHeaders.Insert(new GeneralLedgerHeader
        {
            Id = headerId,
            CompanyId = 1,
            DocumentType = DocumentTypes.PurchaseInvoicePayment,
            Date = BankingTestHarness.Utc(2026, 3, 15)
        });

        purchasing
            .Setup(p => p.PayBill(88, bank.AccountId!.Value, 49.99m, BankingTestHarness.Utc(2026, 3, 15).Date,
                It.IsAny<string?>(), "REF-1", It.IsAny<string?>()))
            .Returns(new VendorPayment
            {
                Id = 55,
                No = "VP-55",
                CompanyId = 1,
                GeneralLedgerHeaderId = headerId,
                Amount = 49.99m
            });

        var bypassed = false;
        harness.Entitlements.Setup(e => e.Bypass()).Returns(() =>
        {
            bypassed = true;
            return Mock.Of<IDisposable>();
        });

        var service = Build(harness, purchasing);
        var result = service.RecordExpense(line.Id, new RecordExpenseFromStatementLineRequest
        {
            NewVendor = new QuickVendorFromStatementRequest { Name = "Figma" },
            ExpenseAccountId = expenseAccountId
        }, "tester");

        Assert.True(bypassed);
        Assert.NotNull(savedBill);
        Assert.Equal(expenseAccountId, savedBill!.PurchaseInvoiceLines.Single().AccountId);
        Assert.Equal(GeneralItemId, savedBill.PurchaseInvoiceLines.Single().ItemId);
        Assert.Equal(7, savedBill.VendorId);
        Assert.NotNull(addedVendor);
        Assert.Same(addedVendor!.PrimaryContact, Assert.Single(addedVendor.VendorContact).Contact);
        Assert.Equal(49.99m, savedBill.PurchaseInvoiceLines.Single().Amount);
        Assert.Equal("bill", result.Bill.Kind);
        Assert.Equal(88, result.Bill.Id);
        Assert.Equal("vendorPayment", result.Payment.Kind);
        Assert.Equal(55, result.Payment.Id);
        Assert.True(result.Line.IsMatched);
        Assert.Equal(bankCr.Id, result.Line.MatchedGeneralLedgerLineId);
        purchasing.Verify(p => p.SaveBill(It.IsAny<PurchaseInvoiceHeader>(), It.IsAny<bool>()), Times.Once);
        purchasing.Verify(p => p.ApproveBill(88, "tester", It.IsAny<DateTime>()), Times.Once);
        purchasing.Verify(p => p.PayBill(88, bank.AccountId!.Value, 49.99m, BankingTestHarness.Utc(2026, 3, 15).Date,
            It.IsAny<string?>(), "REF-1", It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public void RecordExpense_new_vendor_reuses_active_vendor_with_the_same_name()
    {
        using var harness = new BankingTestHarness();
        harness.SeedCashTree();
        SeedExpenseTree(harness);
        harness.Repos.Vendors.Insert(new Vendor
        {
            Id = 3, CompanyId = 1, ArchivedAt = DateTime.UtcNow, Party = new Party { Name = "Figma" }
        });
        harness.Repos.Vendors.Insert(new Vendor { Id = 4, CompanyId = 1, Party = new Party { Name = "figma" } });
        var bank = harness.CreateBank("Checking");
        var line = ImportPayment(harness, bank.Id, -49.99m, "Figma", BankingTestHarness.Utc(2026, 3, 15));

        // Strict: AddVendor is not set up, so creating a vendor would throw.
        var purchasing = new Mock<IPurchasingService>(MockBehavior.Strict);
        PurchaseInvoiceHeader? savedBill = null;
        purchasing
            .Setup(p => p.SaveBill(It.IsAny<PurchaseInvoiceHeader>(), It.IsAny<bool>()))
            .Callback<PurchaseInvoiceHeader, bool>((b, _) => savedBill = b)
            .Throws(new InvalidOperationException("stop after save"));
        harness.Entitlements.Setup(e => e.Bypass()).Returns(Mock.Of<IDisposable>());

        var service = Build(harness, purchasing);
        Assert.Throws<InvalidOperationException>(() => service.RecordExpense(line.Id, new RecordExpenseFromStatementLineRequest
        {
            NewVendor = new QuickVendorFromStatementRequest { Name = " FIGMA " },
            ExpenseAccountId = harness.RequireAccount("50115").Id
        }, "tester"));

        Assert.Equal(4, savedBill!.VendorId);
        purchasing.Verify(p => p.AddVendor(It.IsAny<Vendor>()), Times.Never);
    }

    [Fact]
    public void PayOpenBill_pays_existing_bill_and_matches()
    {
        using var harness = new BankingTestHarness();
        harness.SeedCashTree();
        var bank = harness.CreateBank("Checking");
        var line = ImportPayment(harness, bank.Id, -100m, "Acme", BankingTestHarness.Utc(2026, 4, 1));

        harness.Repos.PurchaseInvoices.Insert(new PurchaseInvoiceHeader
        {
            Id = 12,
            CompanyId = 1,
            VendorId = 1,
            Date = BankingTestHarness.Utc(2026, 3, 20),
            No = "B-12",
            Status = PurchaseInvoiceStatus.Open
        });

        var headerId = harness.Repos.NextGlHeaderId();
        var bankCr = new GeneralLedgerLine
        {
            Id = harness.Repos.NextGlLineId(),
            CompanyId = 1,
            GeneralLedgerHeaderId = headerId,
            AccountId = bank.AccountId!.Value,
            DrCr = DrOrCrSide.Cr,
            Amount = 100m
        };
        harness.Repos.GlLines.Insert(bankCr);

        var purchasing = new Mock<IPurchasingService>(MockBehavior.Strict);
        purchasing
            .Setup(p => p.PayBill(12, bank.AccountId!.Value, 100m, BankingTestHarness.Utc(2026, 4, 1).Date,
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(new VendorPayment
            {
                Id = 9,
                No = "VP-9",
                CompanyId = 1,
                GeneralLedgerHeaderId = headerId,
                Amount = 100m
            });

        var service = Build(harness, purchasing);
        var result = service.PayOpenBill(line.Id, new PayOpenBillFromStatementLineRequest { BillId = 12 }, "tester");

        Assert.Equal(12, result.Bill.Id);
        Assert.Equal(9, result.Payment.Id);
        Assert.Equal(bankCr.Id, result.Line.MatchedGeneralLedgerLineId);
    }

    [Fact]
    public void RecordExpense_remembers_description_rule_and_FindDescriptionRule_returns_it()
    {
        using var harness = new BankingTestHarness();
        harness.SeedCashTree();
        SeedExpenseTree(harness);
        var bank = harness.CreateBank("Checking");
        var line = ImportPayment(harness, bank.Id, -12m, "Figma", BankingTestHarness.Utc(2026, 8, 1));
        var expenseAccountId = harness.RequireAccount("50115").Id;

        var purchasing = new Mock<IPurchasingService>(MockBehavior.Strict);
        purchasing.Setup(p => p.SaveBill(It.IsAny<PurchaseInvoiceHeader>(), It.IsAny<bool>()))
            .Callback<PurchaseInvoiceHeader, bool>((b, _) => { b.Id = 1; b.No = "B1"; harness.Repos.PurchaseInvoices.Insert(b); });
        purchasing.Setup(p => p.ApproveBill(1, It.IsAny<string?>(), It.IsAny<DateTime>()));
        purchasing.Setup(p => p.AddVendor(It.IsAny<Vendor>()))
            .Callback<Vendor>(v => { v.Id = 2; v.CompanyId = 1; harness.Repos.Vendors.Insert(v); });
        var headerId = harness.Repos.NextGlHeaderId();
        harness.Repos.GlLines.Insert(new GeneralLedgerLine
        {
            Id = harness.Repos.NextGlLineId(),
            CompanyId = 1,
            GeneralLedgerHeaderId = headerId,
            AccountId = bank.AccountId!.Value,
            DrCr = DrOrCrSide.Cr,
            Amount = 12m
        });
        purchasing.Setup(p => p.PayBill(1, bank.AccountId!.Value, 12m, It.IsAny<DateTime>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(new VendorPayment { Id = 3, No = "P3", GeneralLedgerHeaderId = headerId, Amount = 12m });
        harness.Entitlements.Setup(e => e.Bypass()).Returns(Mock.Of<IDisposable>());

        var service = Build(harness, purchasing);
        Assert.Null(service.FindDescriptionRule("Figma"));
        service.RecordExpense(line.Id, new RecordExpenseFromStatementLineRequest
        {
            NewVendor = new QuickVendorFromStatementRequest { Name = "Figma" },
            ExpenseAccountId = expenseAccountId,
            RememberRule = true
        }, "t");

        var rule = service.FindDescriptionRule("  figma ");
        Assert.NotNull(rule);
        Assert.Equal("figma", rule!.DescriptionKey);
        Assert.Equal(2, rule.VendorId);
        Assert.Equal(expenseAccountId, rule.ExpenseAccountId);
    }

    [Fact]
    public void BulkRecordExpense_creates_one_bill_per_line()
    {
        using var harness = new BankingTestHarness();
        harness.SeedCashTree();
        SeedExpenseTree(harness);
        var bank = harness.CreateBank("Checking");
        var a = ImportPayment(harness, bank.Id, -10m, "Acme", BankingTestHarness.Utc(2026, 8, 2));
        var b = ImportPayment(harness, bank.Id, -20m, "Acme", BankingTestHarness.Utc(2026, 8, 3));
        var expenseAccountId = harness.RequireAccount("50115").Id;
        harness.Repos.Vendors.Insert(new Vendor { Id = 9, CompanyId = 1 });

        var saveCount = 0;
        var purchasing = new Mock<IPurchasingService>(MockBehavior.Strict);
        purchasing.Setup(p => p.SaveBill(It.IsAny<PurchaseInvoiceHeader>(), It.IsAny<bool>()))
            .Callback<PurchaseInvoiceHeader, bool>((bill, _) =>
            {
                saveCount++;
                bill.Id = saveCount;
                bill.No = $"B{saveCount}";
                harness.Repos.PurchaseInvoices.Insert(bill);
            });
        purchasing.Setup(p => p.ApproveBill(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTime>()));
        purchasing.Setup(p => p.PayBill(It.IsAny<int>(), bank.AccountId!.Value, It.IsAny<decimal>(), It.IsAny<DateTime>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns((int billId, int _, decimal amount, DateTime __, string? ___, string? ____, string? _____) =>
            {
                var headerId = harness.Repos.NextGlHeaderId();
                harness.Repos.GlLines.Insert(new GeneralLedgerLine
                {
                    Id = harness.Repos.NextGlLineId(),
                    CompanyId = 1,
                    GeneralLedgerHeaderId = headerId,
                    AccountId = bank.AccountId!.Value,
                    DrCr = DrOrCrSide.Cr,
                    Amount = amount
                });
                return new VendorPayment { Id = billId + 100, No = $"P{billId}", GeneralLedgerHeaderId = headerId, Amount = amount };
            });
        harness.Entitlements.Setup(e => e.Bypass()).Returns(Mock.Of<IDisposable>());

        var service = Build(harness, purchasing);
        var result = service.BulkRecordExpense(new BulkRecordExpenseFromStatementLinesRequest
        {
            LineIds = new[] { a.Id, b.Id },
            VendorId = 9,
            ExpenseAccountId = expenseAccountId,
            RememberRule = true
        }, "t");

        Assert.Equal(2, result.Results.Count);
        Assert.Equal(2, saveCount);
        Assert.NotNull(result.Rule);
        Assert.Equal("acme", result.Rule!.DescriptionKey);
    }

    [Fact]
    public void RecordExpense_rejects_deposit_lines_and_already_matched()
    {
        using var harness = new BankingTestHarness();
        harness.SeedCashTree();
        SeedExpenseTree(harness);
        var bank = harness.CreateBank("Checking");
        var deposit = ImportPayment(harness, bank.Id, 20m, "Client", BankingTestHarness.Utc(2026, 5, 1));
        var matched = ImportPayment(harness, bank.Id, -10m, "X", BankingTestHarness.Utc(2026, 5, 2));
        matched.MatchedGeneralLedgerLineId = 999;
        harness.Repos.StatementLines.Update(matched);

        var purchasing = new Mock<IPurchasingService>(MockBehavior.Strict);
        var service = Build(harness, purchasing);
        var request = new RecordExpenseFromStatementLineRequest
        {
            VendorId = 1,
            ExpenseAccountId = harness.RequireAccount("50115").Id
        };
        harness.Repos.Vendors.Insert(new Vendor { Id = 1, CompanyId = 1 });

        Assert.Throws<InvalidOperationException>(() => service.RecordExpense(deposit.Id, request, "t"));
        Assert.Throws<InvalidOperationException>(() => service.RecordExpense(matched.Id, request, "t"));
    }


    [Fact]
    public void RecordExpense_with_12_percent_vat_splits_gross_so_bill_total_equals_bank_amount()
    {
        using var harness = new BankingTestHarness();
        harness.SeedCashTree();
        SeedExpenseTree(harness);
        var bank = harness.CreateBank("Checking");
        var gross = 112m;
        var line = ImportPayment(harness, bank.Id, -gross, "Vendor VAT", BankingTestHarness.Utc(2026, 6, 1));
        var expenseAccountId = harness.RequireAccount("50115").Id;
        harness.Repos.Taxes.Insert(new Tax
        {
            Id = 12,
            CompanyId = 1,
            TaxName = "VAT 12%",
            TaxCode = "VAT12",
            Rate = 12m,
            IsActive = true
        });
        harness.Repos.Vendors.Insert(new Vendor { Id = 1, CompanyId = 1 });

        var purchasing = new Mock<IPurchasingService>(MockBehavior.Strict);
        PurchaseInvoiceHeader? savedBill = null;
        purchasing
            .Setup(p => p.SaveBill(It.IsAny<PurchaseInvoiceHeader>(), It.IsAny<bool>()))
            .Callback<PurchaseInvoiceHeader, bool>((b, preserve) =>
            {
                savedBill = b;
                b.Id = 90;
                b.No = "B-90";
                b.CompanyId = 1;
                Assert.True(preserve);
                harness.Repos.PurchaseInvoices.Insert(b);
            });
        purchasing.Setup(p => p.ApproveBill(90, "tester", It.IsAny<DateTime>()));

        var headerId = harness.Repos.NextGlHeaderId();
        var bankCr = new GeneralLedgerLine
        {
            Id = harness.Repos.NextGlLineId(),
            CompanyId = 1,
            GeneralLedgerHeaderId = headerId,
            AccountId = bank.AccountId!.Value,
            DrCr = DrOrCrSide.Cr,
            Amount = gross
        };
        harness.Repos.GlLines.Insert(bankCr);
        harness.Repos.GlHeaders.Insert(new GeneralLedgerHeader
        {
            Id = headerId,
            CompanyId = 1,
            DocumentType = DocumentTypes.PurchaseInvoicePayment,
            Date = BankingTestHarness.Utc(2026, 6, 1)
        });
        purchasing
            .Setup(p => p.PayBill(90, bank.AccountId!.Value, gross, BankingTestHarness.Utc(2026, 6, 1).Date,
                It.IsAny<string?>(), "REF-1", It.IsAny<string?>()))
            .Returns(new VendorPayment
            {
                Id = 56,
                No = "VP-56",
                CompanyId = 1,
                GeneralLedgerHeaderId = headerId,
                Amount = gross
            });
        harness.Entitlements.Setup(e => e.Bypass()).Returns(Mock.Of<IDisposable>());

        var service = Build(harness, purchasing);
        var result = service.RecordExpense(line.Id, new RecordExpenseFromStatementLineRequest
        {
            VendorId = 1,
            ExpenseAccountId = expenseAccountId,
            TaxId = 12
        }, "tester");

        Assert.NotNull(savedBill);
        var billLine = savedBill!.PurchaseInvoiceLines.Single();
        Assert.Equal(100m, billLine.Amount);
        Assert.Equal(12m, billLine.TaxAmount);
        Assert.Equal(gross, savedBill.Total());
        Assert.Equal(56, result.Payment.Id);
        purchasing.Verify(p => p.PayBill(90, bank.AccountId!.Value, gross, BankingTestHarness.Utc(2026, 6, 1).Date,
            It.IsAny<string?>(), "REF-1", It.IsAny<string?>()), Times.Once);
    }


    [Theory]
    [InlineData(12)]
    [InlineData(5)]
    public void SplitInclusive_every_cent_from_0_01_to_100_sums_to_gross(decimal rate)
    {
        for (var cents = 1; cents <= 10_000; cents++)
        {
            var gross = cents / 100m;
            var (net, tax) = PurchaseInvoiceLine.SplitInclusive(gross, rate);
            Assert.Equal(gross, net + tax);
        }
    }

    [Fact]
    public void RecordExpense_preserves_inclusive_tax_for_amounts_not_invertible_via_TaxOf()
    {
        using var harness = new BankingTestHarness();
        harness.SeedCashTree();
        SeedExpenseTree(harness);
        var bank = harness.CreateBank("Checking");
        // 0.05 at 12%: TaxOf(net) cannot recover the inclusive tax; SplitInclusive still sums to gross.
        var gross = 0.05m;
        var line = ImportPayment(harness, bank.Id, -gross, "Tiny", BankingTestHarness.Utc(2026, 6, 2));
        var expenseAccountId = harness.RequireAccount("50115").Id;
        harness.Repos.Taxes.Insert(new Tax
        {
            Id = 12, CompanyId = 1, TaxName = "VAT 12%", TaxCode = "VAT12", Rate = 12m, IsActive = true
        });
        harness.Repos.Vendors.Insert(new Vendor { Id = 1, CompanyId = 1 });

        var purchasing = new Mock<IPurchasingService>(MockBehavior.Strict);
        PurchaseInvoiceHeader? savedBill = null;
        purchasing
            .Setup(p => p.SaveBill(It.IsAny<PurchaseInvoiceHeader>(), true))
            .Callback<PurchaseInvoiceHeader, bool>((b, _) =>
            {
                savedBill = b;
                b.Id = 91;
                b.No = "B-91";
                b.CompanyId = 1;
                harness.Repos.PurchaseInvoices.Insert(b);
            });
        purchasing.Setup(p => p.ApproveBill(91, "tester", It.IsAny<DateTime>()));
        var headerId = harness.Repos.NextGlHeaderId();
        harness.Repos.GlLines.Insert(new GeneralLedgerLine
        {
            Id = harness.Repos.NextGlLineId(),
            CompanyId = 1,
            GeneralLedgerHeaderId = headerId,
            AccountId = bank.AccountId!.Value,
            DrCr = DrOrCrSide.Cr,
            Amount = gross
        });
        purchasing
            .Setup(p => p.PayBill(91, bank.AccountId!.Value, gross, BankingTestHarness.Utc(2026, 6, 2).Date,
                It.IsAny<string?>(), "REF-1", It.IsAny<string?>()))
            .Returns(new VendorPayment
            {
                Id = 57, No = "VP-57", CompanyId = 1, GeneralLedgerHeaderId = headerId, Amount = gross
            });
        harness.Entitlements.Setup(e => e.Bypass()).Returns(Mock.Of<IDisposable>());

        var service = Build(harness, purchasing);
        service.RecordExpense(line.Id, new RecordExpenseFromStatementLineRequest
        {
            VendorId = 1,
            ExpenseAccountId = expenseAccountId,
            TaxId = 12
        }, "tester");

        Assert.NotNull(savedBill);
        Assert.Equal(gross, savedBill!.Total());
        var (expectedNet, expectedTax) = PurchaseInvoiceLine.SplitInclusive(gross, 12m);
        var billLine = savedBill.PurchaseInvoiceLines.Single();
        Assert.Equal(expectedNet, billLine.Amount);
        Assert.Equal(expectedTax, billLine.TaxAmount);
        Assert.NotEqual(expectedTax, PurchaseInvoiceLine.TaxOf(billLine.Amount, 12m));
    }

    [Theory]
    [InlineData("Figma INV-123 2026-03-15", "figma")]
    [InlineData("AWS ref:AB12 fees 99.00", "aws fees")]
    [InlineData("Store 01/02/2026 #44", "store")]
    public void NormalizeDescriptionKey_strips_digits_dates_and_refs(string input, string expected)
    {
        Assert.Equal(expected, StatementLineExpenseService.NormalizeDescriptionKey(input));
    }

    [Fact]
    public void FindDescriptionRule_skips_archived_vendor_or_account()
    {
        using var harness = new BankingTestHarness();
        SeedExpenseTree(harness);
        harness.Repos.Vendors.Insert(new Vendor { Id = 1, CompanyId = 1, ArchivedAt = DateTime.UtcNow });
        harness.Repos.StatementDescriptionRules.Insert(new BankStatementDescriptionRule
        {
            Id = 1,
            CompanyId = 1,
            DescriptionKey = "figma",
            VendorId = 1,
            ExpenseAccountId = harness.RequireAccount("50115").Id,
            CreatedAt = DateTime.UtcNow,
            LastUsedAt = DateTime.UtcNow
        });
        var service = Build(harness, new Mock<IPurchasingService>(MockBehavior.Strict));
        Assert.Null(service.FindDescriptionRule("Figma"));
    }

    private const int GeneralItemId = 5;

    private static StatementLineExpenseService Build(BankingTestHarness harness, Mock<IPurchasingService> purchasing)
    {
        var inventory = new Mock<IInventoryService>(MockBehavior.Strict);
        inventory.Setup(i => i.GetAllItems()).Returns(new List<Item>
        {
            new() { Id = GeneralItemId, CompanyId = 1, Description = StatementIncomeRecorder.GeneralItemName }
        });
        var tenant = new BankingTestHarness.FakeTenant(1);
        var catalog = new DocumentLineAccountCatalog(
            harness.Repos.Accounts,
            harness.Repos.Customers,
            tenant);
        return new StatementLineExpenseService(
            harness.Repos.StatementLines,
            harness.Repos.Banks,
            harness.Repos.Accounts,
            harness.Repos.Vendors,
            harness.Repos.PurchaseInvoices,
            harness.Repos.GlLines,
            harness.Repos.Taxes,
            harness.Repos.StatementDescriptionRules,
            purchasing.Object,
            inventory.Object,
            new Mock<IFinancialService>(MockBehavior.Loose).Object,
            harness.Banks,
            catalog,
            harness.Entitlements.Object,
            tenant);
    }

    private static BankStatementLine ImportPayment(
        BankingTestHarness harness,
        int bankId,
        decimal amount,
        string payee,
        DateTime posted)
    {
        var import = new BankStatementImport
        {
            Id = harness.Repos.StatementImports.Table.Count() + 1,
            CompanyId = 1,
            BankId = bankId,
            Source = BankStatementSources.Csv,
            FileName = "t.csv",
            ImportedAt = DateTime.UtcNow,
            ImportedBy = "t"
        };
        harness.Repos.StatementImports.Insert(import);
        var line = new BankStatementLine
        {
            CompanyId = 1,
            BankId = bankId,
            BankStatementImportId = import.Id,
            Source = BankStatementSources.Csv,
            ExternalId = Guid.NewGuid().ToString("N"),
            PostedDate = posted.Date,
            Amount = amount,
            Payee = payee,
            Reference = "REF-1"
        };
        harness.Repos.StatementLines.Insert(line);
        return line;
    }

    private static void SeedExpenseTree(BankingTestHarness harness)
    {
        void Add(string code, string name, int id, int classId = (int)AccountClasses.Expense) =>
            harness.Repos.Accounts.Insert(new Account
            {
                Id = id,
                CompanyId = 1,
                AccountCode = code,
                AccountName = name,
                AccountClassId = classId,
                DrOrCrSide = DrOrCrSide.Dr,
                IsCash = false
            });

        Add("50111", "Supplies Expenses-Office", 70);
        Add("50112", "Telephone Expenses", 71);
        Add("50115", "Dues and Subscriptions Expenses", 72);
        Add("50117", "Meals and Entertainment Expenses", 73);
        Add("50118", "Travel Expenses", 74);
        Add("50120", "Legal and Professional Expenses", 75);
        Add("50121", "Bank Charges", 76);
        Add("50600", "Other Expenses", 77);
        Add("13302", "Plants & Equipments", 78, (int)AccountClasses.Assets);
    }
}
