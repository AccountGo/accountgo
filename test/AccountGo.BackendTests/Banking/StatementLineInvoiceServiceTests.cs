// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Sales;
using Core.Tenancy;
using Dto.Banking;
using Moq;
using Services.Banking;
using Services.Sales;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class StatementLineInvoiceServiceTests
{
    [Fact]
    public void ApplyOpenInvoice_adds_receipt_allocates_and_matches_bank_debit()
    {
        using var harness = new BankingTestHarness();
        harness.SeedCashTree();
        var bank = harness.CreateBank("Checking");
        var line = SeedDeposit(harness, bank.Id, 200m, "Client Co", BankingTestHarness.Utc(2026, 7, 10));

        var customer = new Customer
        {
            Id = 3,
            CompanyId = 1,
            AccountsReceivableAccountId = 90
        };
        harness.Repos.Accounts.Insert(new Account
        {
            Id = 90,
            CompanyId = 1,
            AccountCode = "10120",
            AccountName = "AR",
            AccountClassId = (int)AccountClasses.Assets,
            DrOrCrSide = DrOrCrSide.Dr
        });

        var invoice = new SalesInvoiceHeader
        {
            Id = 44,
            CompanyId = 1,
            CustomerId = customer.Id,
            Customer = customer,
            Date = BankingTestHarness.Utc(2026, 7, 1),
            No = "INV-44",
            GeneralLedgerHeaderId = 500,
            Status = SalesInvoiceStatus.Open
        };
        invoice.SalesInvoiceLines.Add(new SalesInvoiceLine { Quantity = 1, Amount = 200m });
        harness.Repos.SalesInvoices.Insert(invoice);

        var headerId = harness.Repos.NextGlHeaderId();
        var bankDr = new GeneralLedgerLine
        {
            Id = harness.Repos.NextGlLineId(),
            CompanyId = 1,
            GeneralLedgerHeaderId = headerId,
            AccountId = bank.AccountId!.Value,
            DrCr = DrOrCrSide.Dr,
            Amount = 200m
        };
        harness.Repos.GlLines.Insert(bankDr);

        SalesReceiptHeader? savedReceipt = null;
        var sales = new Mock<ISalesService>(MockBehavior.Strict);
        sales.Setup(s => s.GetSalesInvoiceById(44)).Returns(invoice);
        sales.Setup(s => s.AddSalesReceipt(It.IsAny<SalesReceiptHeader>()))
            .Callback<SalesReceiptHeader>(r =>
            {
                savedReceipt = r;
                r.Id = 77;
                r.No = "R-77";
                r.GeneralLedgerHeaderId = headerId;
                harness.Repos.Receipts.Insert(r);
            });
        sales.Setup(s => s.UpdateSalesInvoice(invoice));

        var service = new StatementLineInvoiceService(
            harness.Repos.StatementLines,
            harness.Repos.Banks,
            harness.Repos.GlLines,
            sales.Object,
            harness.Banks,
            new Services.Sales.InvoicePaymentRecorder(sales.Object),
            new BankingTestHarness.FakeTenant(1));

        var result = service.ApplyOpenInvoice(line.Id, new ApplyOpenInvoiceFromStatementLineRequest
        {
            InvoiceId = 44
        }, "tester");

        Assert.NotNull(savedReceipt);
        Assert.Equal(200m, savedReceipt!.Amount);
        Assert.Equal("invoice", result.Invoice.Kind);
        Assert.Equal(44, result.Invoice.Id);
        Assert.Equal("salesReceipt", result.Receipt.Kind);
        Assert.Equal(77, result.Receipt.Id);
        Assert.Equal(bankDr.Id, result.Line.MatchedGeneralLedgerLineId);
        Assert.Single(invoice.CustomerAllocations);
        sales.Verify(s => s.AddSalesReceipt(It.IsAny<SalesReceiptHeader>()), Times.Once);
        sales.Verify(s => s.UpdateSalesInvoice(invoice), Times.Once);
    }

    [Fact]
    public void ApplyOpenInvoice_rejects_money_out_and_excluded()
    {
        using var harness = new BankingTestHarness();
        harness.SeedCashTree();
        var bank = harness.CreateBank("Checking");
        var payment = SeedDeposit(harness, bank.Id, -50m, "X", BankingTestHarness.Utc(2026, 7, 11));
        var excluded = SeedDeposit(harness, bank.Id, 50m, "Y", BankingTestHarness.Utc(2026, 7, 12));
        excluded.ExcludedAt = DateTime.UtcNow;
        harness.Repos.StatementLines.Update(excluded);

        var sales = new Mock<ISalesService>(MockBehavior.Strict);
        var service = new StatementLineInvoiceService(
            harness.Repos.StatementLines,
            harness.Repos.Banks,
            harness.Repos.GlLines,
            sales.Object,
            harness.Banks,
            new Services.Sales.InvoicePaymentRecorder(sales.Object),
            new BankingTestHarness.FakeTenant(1));

        Assert.Throws<InvalidOperationException>(() =>
            service.ApplyOpenInvoice(payment.Id, new ApplyOpenInvoiceFromStatementLineRequest { InvoiceId = 1 }, "t"));
        Assert.Throws<InvalidOperationException>(() =>
            service.ApplyOpenInvoice(excluded.Id, new ApplyOpenInvoiceFromStatementLineRequest { InvoiceId = 1 }, "t"));
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
            Payee = payee,
            Reference = "REF"
        };
        harness.Repos.StatementLines.Insert(line);
        return line;
    }
}
