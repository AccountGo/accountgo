// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Linq;
using Api.Service;
using Core.Domain;
using Dto.Financial;
using QuestPDF.Fluent;
using Services.Financial;
using UglyToad.PdfPig;
using Xunit;

namespace AccountGo.BackendTests.Financial;

public class FinancialReportPdfTests
{
    static FinancialReportPdfTests() => QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    private static FinancialReportRequest Base(FinancialReportKind kind) => new()
    {
        Kind = kind,
        CompanyName = "Northwind Trading",
        LegalName = "Northwind Trading Company",
        CurrencyCode = "USD",
        Slug = "northwind",
        From = new DateTime(2026, 4, 1),
        To = new DateTime(2026, 4, 30),
        GeneratedAtUtc = new DateTime(2026, 5, 1, 15, 4, 0, DateTimeKind.Utc),
        TimeZoneId = "UTC",
    };

    [Fact]
    public void TrialBalance_NetsPostings_SortsCodes_AndStatesWhetherItBalances()
    {
        var request = Base(FinancialReportKind.TrialBalance);
        request = new FinancialReportRequest
        {
            Kind = request.Kind,
            CompanyName = request.CompanyName,
            CurrencyCode = "USD",
            Slug = "northwind",
            To = request.To,
            From = request.From,
            GeneratedAtUtc = request.GeneratedAtUtc,
            TrialBalance =
            [
                new TrialBalance { AccountCode = "40100", AccountName = "Sales", Debit = 10000m, Credit = 25000m },
                new TrialBalance { AccountCode = "10120", AccountName = "Receivables", Debit = 25000m, Credit = 15000m },
                new TrialBalance { AccountCode = "10111", AccountName = "Bank", Debit = 5000m, Credit = 480m },
                new TrialBalance { AccountCode = "13302", AccountName = "Equipment", Debit = 360m, Credit = 180m },
                new TrialBalance { AccountCode = "50200", AccountName = "Purchases", Debit = 675m, Credit = 375m },
                new TrialBalance { AccountCode = "20110", AccountName = "Payables", Debit = 1035m, Credit = 1035m },
            ],
        };

        var print = FinancialReportComposer.Compose(request);
        var accounts = print.Lines.Where(line => line.Kind == FinancialReportLineKind.Account).Select(line => line.Code).ToArray();
        Assert.Equal(["10111", "10120", "13302", "40100", "50200"], accounts);
        Assert.Equal(4520m, print.Lines[0].Debit);
        Assert.Equal(0m, print.Lines[0].Credit);
        var totals = print.Lines.Single(line => line.Label == "Total Debits / Total Credits");
        Assert.Equal(15000m, totals.Debit);
        Assert.Equal(15000m, totals.Credit);
        Assert.Equal("In balance", print.BalanceStatus);
        Assert.Equal("Trial Balance", print.Title);
        Assert.Equal("April 1, 2026 to April 30, 2026", print.PeriodLabel);
        Assert.Contains("net debit or credit", print.Footnote, StringComparison.OrdinalIgnoreCase);

        var imbalanced = FinancialReportComposer.Compose(new FinancialReportRequest
        {
            Kind = FinancialReportKind.TrialBalance,
            CurrencyCode = "USD",
            CompanyName = "Northwind",
            TrialBalance = [new TrialBalance { AccountCode = "999", AccountName = "Imbalance", Debit = 0.01m, Credit = 0m }],
        });
        Assert.Equal("Out of balance by $0.01", imbalanced.BalanceStatus);
    }

    [Fact]
    public void ProfitAndLoss_ClassifiesCostOfGoods_AndKeepsSignedReturns()
    {
        var print = FinancialReportComposer.Compose(new FinancialReportRequest
        {
            Kind = FinancialReportKind.IncomeStatement,
            CompanyName = "Northwind Trading",
            CurrencyCode = "USD",
            From = new DateTime(2026, 4, 1),
            To = new DateTime(2026, 4, 30),
            Accounts =
            [
                new FinancialReportAccountNode { Id = 10, AccountName = "Cost of Goods Sold" },
                new FinancialReportAccountNode { Id = 11, ParentAccountId = 10, AccountName = "Materials" },
            ],
            Income =
            [
                new IncomeStatement { AccountId = 1, AccountCode = "4", AccountName = "Sales", IsExpense = false, Amount = 1000m },
                new IncomeStatement { AccountId = 2, AccountCode = "41", AccountName = "Returns", IsExpense = false, Amount = -100m },
                new IncomeStatement { AccountId = 11, AccountCode = "5", AccountName = "Materials", IsExpense = true, Amount = 200m },
                new IncomeStatement { AccountId = 12, AccountCode = "6", AccountName = "Rent", IsExpense = true, Amount = 300m },
                new IncomeStatement { AccountId = 13, AccountCode = "7", AccountName = "Unused", IsExpense = true, Amount = 0m },
            ],
        });

        Assert.Equal("Profit and Loss", print.Title);
        Assert.Equal("Income statement", print.Subtitle);
        Assert.Equal(700m, Amount(print, "Gross Profit"));
        Assert.Equal(300m, Amount(print, "Total Operating Expenses"));
        Assert.Equal(400m, Amount(print, "Net Profit"));
        Assert.DoesNotContain(print.Lines, line => line.Label == "Unused");
        Assert.Null(print.BalanceStatus);

        var pdf = Text(print);
        Assert.Contains("Northwind Trading", pdf);
        Assert.Contains("Profit and Loss", pdf);
        Assert.Contains("Gross Profit", pdf);
        Assert.Contains("$700.00", pdf);
        Assert.Contains("($100.00)", pdf);
        Assert.Contains("Unaudited", pdf);
        Assert.Contains("Page", pdf);
    }

    [Fact]
    public void ProfitAndLoss_Comparison_AppendsPriorOnlyAccountsAfterTheStatement()
    {
        FinancialReportRequest Request(decimal sales, bool priorOnly) => new()
        {
            Kind = FinancialReportKind.IncomeStatement,
            CompanyName = "Northwind",
            CurrencyCode = "EUR",
            From = new DateTime(2026, 4, 1),
            To = new DateTime(2026, 4, 30),
            CompareFrom = new DateTime(2025, 4, 1),
            CompareTo = new DateTime(2025, 4, 30),
            Accounts = [new FinancialReportAccountNode { Id = 10, AccountName = "Cost of Goods Sold" }],
            Income = priorOnly
                ? [new IncomeStatement { AccountId = 1, AccountCode = "4", AccountName = "Sales", IsExpense = false, Amount = sales }]
                : [new IncomeStatement { AccountId = 1, AccountCode = "4", AccountName = "Sales", IsExpense = false, Amount = sales }],
            ComparativeIncome =
            [
                new IncomeStatement { AccountId = 1, AccountCode = "4", AccountName = "Sales", IsExpense = false, Amount = 800m },
                new IncomeStatement { AccountId = 9, AccountCode = "42", AccountName = "Service", IsExpense = false, Amount = 50m },
            ],
        };

        var print = FinancialReportComposer.Compose(Request(1000m, false));
        Assert.True(print.ShowComparison);
        Assert.Equal("1 April 2026 to 30 April 2026", print.PeriodLabel);
        var sales = print.Lines.Single(line => line.Label == "Sales");
        Assert.Equal(1000m, sales.Amount);
        Assert.Equal(800m, sales.ComparativeAmount);
        Assert.Equal(200m, sales.Change);
        Assert.Equal(25m, sales.ChangePercent);
        var labels = print.Lines.Select(line => line.Label).ToList();
        Assert.True(labels.IndexOf("Service") > labels.IndexOf("Net Profit"));
        var service = print.Lines.Single(line => line.Label == "Service");
        Assert.Equal(0m, service.Amount);
        Assert.Equal(50m, service.ComparativeAmount);

        var pdf = Text(print);
        Assert.Contains("€1,000.00", pdf);
        Assert.Contains("25.0%", pdf);
        Assert.Contains("Prior period", pdf);
    }

    [Fact]
    public void BalanceSheet_KeepsContraAssetsAndUnclosedLossesSigned()
    {
        var print = FinancialReportComposer.Compose(new FinancialReportRequest
        {
            Kind = FinancialReportKind.BalanceSheet,
            CompanyName = "Northwind Trading",
            LegalName = "Northwind Trading Company",
            CurrencyCode = "USD",
            To = new DateTime(2026, 4, 30),
            BalanceSheet =
            [
                new BalanceSheet { AccountCode = "1", AccountName = "Cash", AccountClassId = 1, Amount = 100m },
                new BalanceSheet { AccountCode = "2", AccountName = "Depreciation", AccountClassId = 1, Amount = -20m },
                new BalanceSheet { AccountCode = "3", AccountName = "Payables", AccountClassId = 2, Amount = 30m },
                new BalanceSheet { AccountCode = "4", AccountName = "Capital", AccountClassId = 3, Amount = 100m },
                new BalanceSheet { AccountCode = "", AccountName = "Unclosed loss", AccountClassId = 3, Amount = -50m },
            ],
        });

        Assert.Equal("Balance Sheet", print.Title);
        Assert.Equal("As of April 30, 2026", print.PeriodLabel);
        Assert.Equal("Northwind Trading Company", print.Heading);
        Assert.Equal("Northwind Trading", print.TradingAs);
        Assert.Equal(80m, Amount(print, "Total Assets"));
        Assert.Equal(50m, Amount(print, "Total Equity"));
        Assert.Equal(80m, Amount(print, "Total Liabilities + Equity"));
        Assert.Equal(0m, Amount(print, "Difference"));
        Assert.Equal("In balance", print.BalanceStatus);
        Assert.Contains("unclosed earnings", print.Footnote, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("($20.00)", Text(print));
    }

    [Fact]
    public void PartnerEquityStatement_WithEightPartners_UsesLandscapeAndRepeatsMovementTables()
    {
        var columns = Enumerable.Range(1, 8)
            .Select(id => new EquityStatementColumnDto($"partner:{id}", $"Partner {id}"))
            .Append(new EquityStatementColumnDto("unallocated", "Unallocated"))
            .ToList();
        var amounts = columns.ToDictionary(column => column.Key, _ => 0m);
        var print = FinancialReportComposer.Compose(new FinancialReportRequest
        {
            Kind = FinancialReportKind.EquityStatement,
            CompanyName = "Northwind Trading",
            CurrencyCode = "USD",
            From = new DateTime(2026, 1, 1),
            To = new DateTime(2026, 12, 31),
            EquityStatement = new EquityStatementDto
            {
                EntityType = "Partnership",
                PartnerMode = true,
                From = new DateTime(2026, 1, 1),
                To = new DateTime(2026, 12, 31),
                Columns = columns,
                Rows =
                [
                    new EquityStatementRowDto { Key = "opening", Label = "Opening balance", Amounts = new(amounts), Total = 0m },
                    new EquityStatementRowDto { Key = "closing", Label = "Closing balance", Amounts = new(amounts), Total = 0m },
                ],
            },
        });

        var bytes = GenerateExtensions.GeneratePdf(new FinancialReportPdfDocument(print));
        using var document = PdfDocument.Open(bytes);
        var page = Assert.Single(document.GetPages());
        Assert.True(page.Width > page.Height);
        Assert.True(page.Text.Split("Movement", StringSplitOptions.None).Length - 1 >= 2);
        Assert.Contains("Partner 8", page.Text);
        Assert.Contains("Unallocated", page.Text);
    }

    [Fact]
    public void CashFlow_ReconcilesSections_AndPrintsTheIndirectStatement()
    {
        var print = FinancialReportComposer.Compose(new FinancialReportRequest
        {
            Kind = FinancialReportKind.CashFlow,
            CompanyName = "Northwind Trading",
            CurrencyCode = "GBP",
            From = new DateTime(2026, 4, 1),
            To = new DateTime(2026, 4, 30),
            Slug = "north wind",
            CashFlow = new CashFlowStatement
            {
                AccountingBasis = AccountingBasis.Accrual,
                NetIncome = 500m,
                OperatingCashFlow = 450m,
                InvestingCashFlow = -200m,
                FinancingCashFlow = 100m,
                NetChangeInCash = 350m,
                StartingCash = 1000m,
                EndingCash = 1350m,
                OperatingActivities =
                [
                    new CashFlowLine { AccountCode = "14001", AccountName = "Depreciation add-back", Amount = 50m },
                    new CashFlowLine { AccountCode = "10120", AccountName = "Accounts Receivable", Amount = -100m },
                    new CashFlowLine { AccountCode = "99999", AccountName = "Unused", Amount = 0m },
                ],
                InvestingActivities = [new CashFlowLine { AccountCode = "13302", AccountName = "Equipment Purchase", Amount = -200m }],
                FinancingActivities = [new CashFlowLine { AccountCode = "30100", AccountName = "Member Capital Contribution", Amount = 100m }],
            },
        });

        Assert.Equal("Statement of Cash Flows", print.Title);
        Assert.Equal("Indirect method", print.Subtitle);
        Assert.Equal(450m, Amount(print, "Net cash provided by operating activities"));
        Assert.Equal(-200m, Amount(print, "Net cash provided by investing activities"));
        Assert.Equal(100m, Amount(print, "Net cash provided by financing activities"));
        Assert.Equal(350m, Amount(print, "Net increase / (decrease) in cash"));
        Assert.Equal(1000m, Amount(print, "Cash at beginning of period"));
        Assert.Equal(1350m, Amount(print, "Cash at end of period"));
        Assert.Equal(0m, Amount(print, "Difference"));
        Assert.DoesNotContain(print.Lines, line => line.Label == "Unused");
        Assert.Equal("north-wind-cash-flow-accrual-2026-04-30", print.FileStem);
        Assert.Contains("£1,350.00", Text(print));
    }

    [Fact]
    public void CashBasis_PrintsTheNoticeAndStillTiesTheCashBridge()
    {
        var print = FinancialReportComposer.Compose(new FinancialReportRequest
        {
            Kind = FinancialReportKind.CashFlow,
            CompanyName = "Northwind",
            CurrencyCode = "USD",
            Basis = AccountingBasis.Cash,
            CashFlow = new CashFlowStatement
            {
                AccountingBasis = AccountingBasis.Cash,
                Message = "Statement of Cash Flows is designed for Accrual basis.",
                StartingCash = 500m,
                EndingCash = 500m,
            },
        });

        Assert.Equal("Cash basis", print.BasisLabel);
        Assert.Equal("Statement of Cash Flows is designed for Accrual basis.", print.Notice);
        Assert.Equal(500m, Amount(print, "Cash at beginning of period"));
        Assert.Equal(500m, Amount(print, "Cash at end of period"));
        Assert.Equal(0m, Amount(print, "Difference"));
        Assert.DoesNotContain(print.Lines, line => line.Label.Contains("operating activities", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PersonalBooks_UseTheRegisterAndPlainLanguageStatements()
    {
        var movement = FinancialReportComposer.Compose(new FinancialReportRequest
        {
            Kind = FinancialReportKind.CashFlow,
            Personal = true,
            CompanyName = "Pat",
            CurrencyCode = "PHP",
            From = new DateTime(2026, 2, 1),
            To = new DateTime(2026, 2, 28),
            CashMovement = new FinancialReportCashMovement
            {
                OpeningCash = 100m,
                TotalIn = 25m,
                TotalOut = 0m,
                ClosingCash = 125m,
                Lines =
                [
                    new FinancialReportCashMovementLine
                    {
                        Date = new DateTime(2026, 2, 3),
                        Description = "Income",
                        AccountName = "Bank",
                        CounterpartAccountName = "Pay",
                        MoneyIn = 25m,
                    },
                ],
            },
        });
        Assert.Equal("Cash movement", movement.Title);
        Assert.Null(movement.EmptyNote);

        var emptyRegister = FinancialReportComposer.Compose(new FinancialReportRequest
        {
            Kind = FinancialReportKind.CashFlow,
            Personal = true,
            CompanyName = "Pat",
            CurrencyCode = "USD",
            CashMovement = new FinancialReportCashMovement(),
        });
        Assert.Null(emptyRegister.EmptyNote);
        Assert.NotNull(emptyRegister.CashMovement);
        Assert.Equal(125m, movement.CashMovement!.ClosingCash);
        Assert.Null(movement.BalanceStatus);
        var pdf = Text(movement);
        Assert.Contains("Cash movement", pdf);
        Assert.Contains("PHP 125.00", pdf);
        Assert.Contains("Income", pdf);
        Assert.DoesNotContain("operating", pdf, StringComparison.OrdinalIgnoreCase);

        var worth = FinancialReportComposer.Compose(new FinancialReportRequest
        {
            Kind = FinancialReportKind.BalanceSheet,
            Personal = true,
            CompanyName = "Pat",
            CurrencyCode = "USD",
            To = new DateTime(2026, 2, 28),
            BalanceSheet = [new BalanceSheet { AccountClassId = 1, AccountCode = "1000", AccountName = "Cash", Amount = 10m }],
        });
        Assert.Equal("Net worth (book value)", worth.Title);
        Assert.Contains(worth.Lines, line => line.Label == "Total debts and net worth");

        var compared = FinancialReportComposer.Compose(new FinancialReportRequest
        {
            Kind = FinancialReportKind.IncomeStatement,
            Personal = true,
            CompanyName = "Pat",
            CurrencyCode = "USD",
            From = new DateTime(2026, 4, 1),
            To = new DateTime(2026, 4, 30),
            CompareFrom = new DateTime(2025, 4, 1),
            CompareTo = new DateTime(2025, 4, 30),
            Income = [new IncomeStatement { AccountId = 1, AccountCode = "40100", AccountName = "Pay", IsExpense = false, Amount = 100m }],
            ComparativeIncome =
            [
                new IncomeStatement { AccountId = 1, AccountCode = "40100", AccountName = "Pay", IsExpense = false, Amount = 80m },
                new IncomeStatement { AccountId = 7, AccountCode = "40900", AccountName = "Transfer", IsExpense = false, Amount = 40m },
            ],
        });
        var freelance = Assert.Single(compared.Lines, line => line.Label == "Money from my freelance work");
        Assert.Equal(0m, freelance.Amount);
        Assert.Equal(40m, freelance.ComparativeAmount);
        var order = compared.Lines.Select(line => line.Label).ToList();
        Assert.True(order.IndexOf("Money from my freelance work") < order.IndexOf("Spending"));
        Assert.True(order.IndexOf("Money from my freelance work") > order.IndexOf("Total Earned income"));
    }

    private static decimal? Amount(FinancialReportPrint print, string label) =>
        print.Lines.Single(line => line.Label == label).Amount;

    private static string Text(FinancialReportPrint print)
    {
        var bytes = GenerateExtensions.GeneratePdf(new FinancialReportPdfDocument(print));
        Assert.True(bytes.Length > 100);
        Assert.Equal((byte)'%', bytes[0]);
        using var document = PdfDocument.Open(bytes);
        Assert.Equal("AccountGo", document.Information.Creator);
        Assert.Equal(print.Heading, document.Information.Author);
        return string.Join("\n", document.GetPages().Select(page => page.Text));
    }
}
