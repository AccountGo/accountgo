// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Sales;
using Core.Entitlements;
using Core.Tenancy;
using Dto.Banking;
using Moq;
using Services.Banking;
using Services.Financial;
using Services.Inventory;
using Services.Sales;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class StatementIncomeRecorderTests
{
    [Fact]
    public void Record_creates_invoice_with_Origin_receipt_and_matches_with_kind_invoice()
    {
        using var harness = new BankingTestHarness();
        harness.SeedCashTree();
        SeedRevenue(harness);
        var bank = harness.CreateBank("Checking");
        var line = SeedDeposit(harness, bank.Id, 75m, "Side gig", BankingTestHarness.Utc(2026, 9, 1));

        var item = new Item
        {
            Id = 5,
            CompanyId = 1,
            Description = StatementIncomeRecorder.GeneralItemName,
            SellMeasurementId = 1,
            SmallestMeasurementId = 1,
            SalesAccountId = harness.RequireAccount("40600").Id
        };
        var customer = new Customer
        {
            Id = 8,
            CompanyId = 1,
            IsSystem = true,
            AccountsReceivableAccountId = 91,
            Party = new Party { Name = StatementIncomeRecorder.OtherIncomeCustomerName }
        };
        harness.Repos.Accounts.Insert(new Account
        {
            Id = 91,
            CompanyId = 1,
            AccountCode = "10120",
            AccountName = "AR",
            AccountClassId = (int)AccountClasses.Assets,
            DrOrCrSide = DrOrCrSide.Dr
        });
        harness.Repos.Customers.Insert(customer);

        SalesInvoiceHeader? savedInvoice = null;
        var sales = new Mock<ISalesService>(MockBehavior.Strict);
        sales.Setup(s => s.SaveSalesInvoice(It.IsAny<SalesInvoiceHeader>(), It.IsAny<SalesOrderHeader>()))
            .Callback<SalesInvoiceHeader, SalesOrderHeader>((inv, _) =>
            {
                savedInvoice = inv;
                inv.Id = 33;
                inv.No = "INV-33";
                inv.CompanyId = 1;
                harness.Repos.SalesInvoices.Insert(inv);
            });
        sales.Setup(s => s.PostSalesInvoice(33));
        sales.Setup(s => s.GetSalesInvoiceById(33)).Returns(() =>
        {
            savedInvoice!.Customer = customer;
            savedInvoice.GeneralLedgerHeaderId = 1;
            return savedInvoice;
        });
        sales.Setup(s => s.AddSalesReceipt(It.IsAny<SalesReceiptHeader>()))
            .Callback<SalesReceiptHeader>(r =>
            {
                r.Id = 44;
                r.No = "R-44";
                var headerId = harness.Repos.NextGlHeaderId();
                r.GeneralLedgerHeaderId = headerId;
                harness.Repos.GlLines.Insert(new GeneralLedgerLine
                {
                    Id = harness.Repos.NextGlLineId(),
                    CompanyId = 1,
                    GeneralLedgerHeaderId = headerId,
                    AccountId = bank.AccountId!.Value,
                    DrCr = DrOrCrSide.Dr,
                    Amount = 75m
                });
                harness.Repos.Receipts.Insert(r);
            });
        sales.Setup(s => s.UpdateSalesInvoice(It.IsAny<SalesInvoiceHeader>()));
        sales.Setup(s => s.AddCustomer(It.IsAny<Customer>()));

        var inventory = new Mock<IInventoryService>(MockBehavior.Strict);
        inventory.Setup(i => i.GetAllItems()).Returns(new List<Item> { item });

        var financial = new Mock<IFinancialService>(MockBehavior.Loose);
        financial.Setup(f => f.GetAccounts()).Returns(harness.Repos.Accounts.Table.ToList());

        harness.Entitlements.Setup(e => e.Bypass()).Returns(Mock.Of<IDisposable>());

        var recorder = Build(harness, sales, inventory, financial);

        var result = recorder.Record(line.Id, new RecordOtherIncomeFromStatementLineRequest
        {
            IncomeAccountId = harness.RequireAccount("40600").Id
        }, "tester");

        Assert.NotNull(savedInvoice);
        Assert.Equal(SalesInvoiceOrigins.StatementOtherIncome, savedInvoice!.Origin);
        Assert.Equal("invoice", result.Document.Kind);
        Assert.Equal(33, result.Document.Id);
        Assert.Equal("salesReceipt", result.Receipt.Kind);
        Assert.True(result.Line.IsMatched);
        sales.Verify(s => s.SaveSalesInvoice(It.IsAny<SalesInvoiceHeader>(), It.IsAny<SalesOrderHeader>()), Times.Once);
        sales.Verify(s => s.PostSalesInvoice(33), Times.Once);
        sales.Verify(s => s.AddSalesReceipt(It.IsAny<SalesReceiptHeader>()), Times.Once);
    }

    [Fact]
    public void ListIncomeCategories_maps_curated_revenue_accounts()
    {
        using var harness = new BankingTestHarness();
        SeedRevenue(harness);
        var sales = new Mock<ISalesService>(MockBehavior.Strict);
        var inventory = new Mock<IInventoryService>(MockBehavior.Strict);
        var financial = new Mock<IFinancialService>(MockBehavior.Loose);

        var recorder = Build(harness, sales, inventory, financial);

        var dto = recorder.ListIncomeCategories();
        Assert.Contains(dto.Curated, c => c.Name == "Other income");
        Assert.Contains(dto.Curated, c => c.Name == "Interest income");
    }


    [Fact]
    public void Record_rejects_non_null_TaxId()
    {
        using var harness = new BankingTestHarness();
        harness.SeedCashTree();
        SeedRevenue(harness);
        var bank = harness.CreateBank("Checking");
        var line = SeedDeposit(harness, bank.Id, 10m, "X", BankingTestHarness.Utc(2026, 9, 2));
        var sales = new Mock<ISalesService>(MockBehavior.Strict);
        var inventory = new Mock<IInventoryService>(MockBehavior.Strict);
        var financial = new Mock<IFinancialService>(MockBehavior.Loose);
        var recorder = Build(harness, sales, inventory, financial);
        Assert.Throws<InvalidOperationException>(() => recorder.Record(line.Id, new RecordOtherIncomeFromStatementLineRequest
        {
            IncomeAccountId = harness.RequireAccount("40600").Id,
            TaxId = 1
        }, "t"));
    }

    [Fact]
    public void Record_captures_seller_bill_to_and_payment_instructions_at_issue()
    {
        using var harness = new BankingTestHarness();
        harness.SeedCashTree();
        SeedRevenue(harness);
        var bank = harness.CreateBank("Checking");
        var line = SeedDeposit(harness, bank.Id, 20m, "Gig", BankingTestHarness.Utc(2026, 9, 3));
        harness.Repos.Accounts.Insert(new Account
        {
            Id = 91, CompanyId = 1, AccountCode = "10120", AccountName = "AR",
            AccountClassId = (int)AccountClasses.Assets, DrOrCrSide = DrOrCrSide.Dr
        });
        var customer = new Customer
        {
            Id = 8, CompanyId = 1, IsSystem = true, AccountsReceivableAccountId = 91,
            Party = new Party { Name = StatementIncomeRecorder.OtherIncomeCustomerName }
        };
        harness.Repos.Customers.Insert(customer);
        var item = new Item
        {
            Id = 5, CompanyId = 1, Description = StatementIncomeRecorder.GeneralItemName,
            SellMeasurementId = 1, SmallestMeasurementId = 1,
            SalesAccountId = harness.RequireAccount("40600").Id
        };

        SalesInvoiceHeader? saved = null;
        var sales = new Mock<ISalesService>(MockBehavior.Strict);
        sales.Setup(s => s.SaveSalesInvoice(It.IsAny<SalesInvoiceHeader>(), It.IsAny<SalesOrderHeader>()))
            .Callback<SalesInvoiceHeader, SalesOrderHeader>((inv, _) =>
            {
                saved = inv; inv.Id = 50; inv.No = "INV-50"; inv.CompanyId = 1;
                harness.Repos.SalesInvoices.Insert(inv);
            });
        sales.Setup(s => s.PostSalesInvoice(50));
        sales.Setup(s => s.GetSalesInvoiceById(50)).Returns(() => { saved!.Customer = customer; return saved; });
        sales.Setup(s => s.AddSalesReceipt(It.IsAny<SalesReceiptHeader>()))
            .Callback<SalesReceiptHeader>(r =>
            {
                r.Id = 60; r.No = "R60";
                var hid = harness.Repos.NextGlHeaderId();
                r.GeneralLedgerHeaderId = hid;
                harness.Repos.GlLines.Insert(new GeneralLedgerLine
                {
                    Id = harness.Repos.NextGlLineId(), CompanyId = 1, GeneralLedgerHeaderId = hid,
                    AccountId = bank.AccountId!.Value, DrCr = DrOrCrSide.Dr, Amount = 20m
                });
            });
        sales.Setup(s => s.UpdateSalesInvoice(It.IsAny<SalesInvoiceHeader>()));
        var inventory = new Mock<IInventoryService>(MockBehavior.Strict);
        inventory.Setup(i => i.GetAllItems()).Returns(new List<Item> { item });
        var financial = new Mock<IFinancialService>(MockBehavior.Loose);
        harness.Entitlements.Setup(e => e.Bypass()).Returns(Mock.Of<IDisposable>());

        var tenant = new BankingTestHarness.FakeTenant(1);
        var catalog = new DocumentLineAccountCatalog(harness.Repos.Accounts, harness.Repos.Customers, tenant);
        var snapshots = new Mock<IIssuedInvoiceSnapshotCapture>(MockBehavior.Strict);
        snapshots.Setup(s => s.CaptureSellerAndBillToIfIssued(It.IsAny<SalesInvoiceHeader>()));
        snapshots.Setup(s => s.CapturePaymentInstructionsIfIssued(It.IsAny<SalesInvoiceHeader>())).Returns(true);
        var recorder = new StatementIncomeRecorder(
            harness.Repos.StatementLines, harness.Repos.Banks, harness.Repos.Customers,
            harness.Repos.GlLines, sales.Object, inventory.Object, financial.Object, harness.Banks,
            catalog, snapshots.Object, new InvoicePaymentRecorder(sales.Object),
            harness.Entitlements.Object, tenant);

        recorder.Record(line.Id, new RecordOtherIncomeFromStatementLineRequest
        {
            IncomeAccountId = harness.RequireAccount("40600").Id
        }, "t");

        snapshots.Verify(s => s.CaptureSellerAndBillToIfIssued(It.IsAny<SalesInvoiceHeader>()), Times.Once);
        snapshots.Verify(s => s.CapturePaymentInstructionsIfIssued(It.IsAny<SalesInvoiceHeader>()), Times.Once);
    }


    private static StatementIncomeRecorder Build(
        BankingTestHarness harness,
        Mock<ISalesService> sales,
        Mock<IInventoryService> inventory,
        Mock<IFinancialService> financial)
    {
        var tenant = new BankingTestHarness.FakeTenant(1);
        var catalog = new DocumentLineAccountCatalog(
            harness.Repos.Accounts, harness.Repos.Customers, tenant);
        var snapshots = new Mock<IIssuedInvoiceSnapshotCapture>(MockBehavior.Loose);
        var payments = new InvoicePaymentRecorder(sales.Object);
        return new StatementIncomeRecorder(
            harness.Repos.StatementLines,
            harness.Repos.Banks,
            harness.Repos.Customers,
            harness.Repos.GlLines,
            sales.Object,
            inventory.Object,
            financial.Object,
            harness.Banks,
            catalog,
            snapshots.Object,
            payments,
            harness.Entitlements.Object,
            tenant);
    }

    private static void SeedRevenue(BankingTestHarness harness)
    {
        void Add(string code, string name, int id) =>
            harness.Repos.Accounts.Insert(new Account
            {
                Id = id,
                CompanyId = 1,
                AccountCode = code,
                AccountName = name,
                AccountClassId = (int)AccountClasses.Revenue,
                DrOrCrSide = DrOrCrSide.Cr,
                IsCash = false
            });
        Add("40100", "Sales A/C", 80);
        Add("40300", "Interest Income", 81);
        Add("40600", "Other Income", 82);
    }

    private static BankStatementLine SeedDeposit(
        BankingTestHarness harness,
        int bankId,
        decimal amount,
        string payee,
        DateTime posted)
    {
        var import = new BankStatementImport
        {
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
            Payee = payee
        };
        harness.Repos.StatementLines.Insert(line);
        return line;
    }
}
