// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain;
using Core.Domain.Financials;
using Xunit;

namespace AccountGo.BackendTests.Financial;

/// <summary>
/// Finance-required posting examples for the Individual personal/household bookkeeping model.
/// Ordinary spending stays in expenses; asset moves are transfers; borrowing is liability, not income.
/// </summary>
public class PersonalBookkeepingPostingTests
{
    private static DateTime D(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private sealed class Ledger
    {
        private readonly List<Account> _accounts = new();
        private readonly List<GeneralLedgerLine> _lines = new();

        public int CheckingId { get; }
        public int SavingsId { get; }
        public int WalletId { get; }
        public int LoanId { get; }
        public int CreditCardId { get; }
        public int SalaryId { get; }
        public int GroceriesId { get; }
        public int InterestId { get; }
        public int NetWorthId { get; }
        public int ObeId { get; }

        public Ledger()
        {
            CheckingId = 10;
            SavingsId = 11;
            WalletId = 12;
            LoanId = 20;
            CreditCardId = 21;
            NetWorthId = 30;
            ObeId = 31;
            SalaryId = 40;
            GroceriesId = 50;
            InterestId = 51;

            void Add(int id, string code, string name, int classId, DrOrCrSide side) =>
                _accounts.Add(new Account
                {
                    Id = id,
                    CompanyId = 1,
                    AccountCode = code,
                    AccountName = name,
                    AccountClassId = classId,
                    DrOrCrSide = side
                });

            Add(CheckingId, "10110", "Checking", 1, DrOrCrSide.Dr);
            Add(SavingsId, "10120", "Savings", 1, DrOrCrSide.Dr);
            Add(WalletId, "10111", "Cash wallet", 1, DrOrCrSide.Dr);
            Add(LoanId, "20100", "Personal loan", 2, DrOrCrSide.Cr);
            Add(CreditCardId, "20110", "Credit card", 2, DrOrCrSide.Cr);
            Add(NetWorthId, "30100", "Accumulated net worth", 3, DrOrCrSide.Cr);
            Add(ObeId, "30700", "Opening Balance Equity", 3, DrOrCrSide.Cr);
            Add(SalaryId, "40100", "Salary", 4, DrOrCrSide.Cr);
            Add(GroceriesId, "50100", "Groceries", 5, DrOrCrSide.Dr);
            Add(InterestId, "50200", "Loan interest", 5, DrOrCrSide.Dr);
        }

        public IReadOnlyList<GeneralLedgerLine> Lines => _lines;

        public void Post(DateTime date, int debitAccountId, int creditAccountId, decimal amount, string description)
        {
            var header = new GeneralLedgerHeader
            {
                Id = _lines.Count / 2 + 1,
                CompanyId = 1,
                Date = date,
                DocumentType = DocumentTypes.JournalEntry,
                Description = description
            };
            _lines.Add(new GeneralLedgerLine
            {
                Id = _lines.Count + 1,
                CompanyId = 1,
                AccountId = debitAccountId,
                Account = _accounts.Single(a => a.Id == debitAccountId),
                DrCr = DrOrCrSide.Dr,
                Amount = amount,
                GeneralLedgerHeaderId = header.Id,
                GeneralLedgerHeader = header
            });
            _lines.Add(new GeneralLedgerLine
            {
                Id = _lines.Count + 1,
                CompanyId = 1,
                AccountId = creditAccountId,
                Account = _accounts.Single(a => a.Id == creditAccountId),
                DrCr = DrOrCrSide.Cr,
                Amount = amount,
                GeneralLedgerHeaderId = header.Id,
                GeneralLedgerHeader = header
            });
        }

        public decimal Balance(int accountId)
        {
            var account = _accounts.Single(a => a.Id == accountId);
            var netDr = _lines.Where(l => l.AccountId == accountId && l.DrCr == DrOrCrSide.Dr).Sum(l => l.Amount)
                - _lines.Where(l => l.AccountId == accountId && l.DrCr == DrOrCrSide.Cr).Sum(l => l.Amount);
            return account.DrOrCrSide == DrOrCrSide.Dr ? netDr : -netDr;
        }

        public decimal ExpenseTotal()
        {
            var expenseIds = _accounts.Where(a => a.AccountClassId == 5).Select(a => a.Id).ToHashSet();
            return _lines.Where(l => expenseIds.Contains(l.AccountId) && l.DrCr == DrOrCrSide.Dr).Sum(l => l.Amount)
                - _lines.Where(l => expenseIds.Contains(l.AccountId) && l.DrCr == DrOrCrSide.Cr).Sum(l => l.Amount);
        }

        public decimal IncomeTotal()
        {
            var incomeIds = _accounts.Where(a => a.AccountClassId == 4).Select(a => a.Id).ToHashSet();
            return _lines.Where(l => incomeIds.Contains(l.AccountId) && l.DrCr == DrOrCrSide.Cr).Sum(l => l.Amount)
                - _lines.Where(l => incomeIds.Contains(l.AccountId) && l.DrCr == DrOrCrSide.Dr).Sum(l => l.Amount);
        }
    }

    [Fact]
    public void Salary_posts_as_income_not_equity_or_liability()
    {
        var ledger = new Ledger();
        ledger.Post(D(2025, 6, 15), ledger.CheckingId, ledger.SalaryId, 2000m, "Salary");

        Assert.Equal(2000m, ledger.Balance(ledger.CheckingId));
        Assert.Equal(2000m, ledger.IncomeTotal());
        Assert.Equal(0m, ledger.ExpenseTotal());
        Assert.Equal(0m, ledger.Balance(ledger.NetWorthId));
        Assert.Equal(0m, ledger.Balance(ledger.LoanId));
    }

    [Fact]
    public void Groceries_post_as_expense_not_drawings()
    {
        var ledger = new Ledger();
        ledger.Post(D(2025, 1, 1), ledger.CheckingId, ledger.ObeId, 500m, "Opening cash");
        ledger.Post(D(2025, 7, 1), ledger.GroceriesId, ledger.CheckingId, 70m, "Groceries");

        Assert.Equal(70m, ledger.ExpenseTotal());
        Assert.Equal(430m, ledger.Balance(ledger.CheckingId));
        Assert.DoesNotContain(ledger.Lines, l => l.AccountId == ledger.NetWorthId && l.DrCr == DrOrCrSide.Dr);
    }

    [Fact]
    public void Checking_to_savings_and_bank_to_wallet_are_transfers_not_expense()
    {
        var ledger = new Ledger();
        ledger.Post(D(2025, 1, 1), ledger.CheckingId, ledger.ObeId, 1000m, "Opening checking");
        ledger.Post(D(2025, 2, 1), ledger.SavingsId, ledger.CheckingId, 400m, "Transfer checking to savings");
        ledger.Post(D(2025, 2, 2), ledger.WalletId, ledger.CheckingId, 50m, "Transfer bank to cash wallet");

        Assert.Equal(550m, ledger.Balance(ledger.CheckingId));
        Assert.Equal(400m, ledger.Balance(ledger.SavingsId));
        Assert.Equal(50m, ledger.Balance(ledger.WalletId));
        Assert.Equal(0m, ledger.ExpenseTotal());
        Assert.Equal(0m, ledger.IncomeTotal());
    }

    [Fact]
    public void Loan_proceeds_increase_liability_not_income()
    {
        var ledger = new Ledger();
        ledger.Post(D(2025, 3, 1), ledger.CheckingId, ledger.LoanId, 5000m, "Loan proceeds");

        Assert.Equal(5000m, ledger.Balance(ledger.CheckingId));
        Assert.Equal(5000m, ledger.Balance(ledger.LoanId));
        Assert.Equal(0m, ledger.IncomeTotal());
    }

    [Fact]
    public void Loan_repayment_reduces_principal_liability_and_posts_interest_as_expense()
    {
        var ledger = new Ledger();
        ledger.Post(D(2025, 3, 1), ledger.CheckingId, ledger.LoanId, 5000m, "Loan proceeds");
        ledger.Post(D(2025, 4, 1), ledger.LoanId, ledger.CheckingId, 200m, "Loan principal");
        ledger.Post(D(2025, 4, 1), ledger.InterestId, ledger.CheckingId, 40m, "Loan interest");

        Assert.Equal(4800m, ledger.Balance(ledger.LoanId));
        Assert.Equal(4760m, ledger.Balance(ledger.CheckingId));
        Assert.Equal(40m, ledger.ExpenseTotal());
        Assert.Equal(0m, ledger.IncomeTotal());
    }

    [Fact]
    public void Credit_card_purchase_then_payment_clears_liability_without_double_expense()
    {
        var ledger = new Ledger();
        ledger.Post(D(2025, 1, 1), ledger.CheckingId, ledger.ObeId, 500m, "Opening checking");
        ledger.Post(D(2025, 5, 1), ledger.GroceriesId, ledger.CreditCardId, 80m, "Groceries on card");
        Assert.Equal(80m, ledger.ExpenseTotal());
        Assert.Equal(80m, ledger.Balance(ledger.CreditCardId));

        ledger.Post(D(2025, 5, 15), ledger.CreditCardId, ledger.CheckingId, 80m, "Credit card payment");

        Assert.Equal(0m, ledger.Balance(ledger.CreditCardId));
        Assert.Equal(420m, ledger.Balance(ledger.CheckingId));
        Assert.Equal(80m, ledger.ExpenseTotal());
    }

    [Fact]
    public void Household_shares_one_accumulated_net_worth_perimeter()
    {
        var profile = EntityEquityMapping.GetProfile(EntityType.Individual, allowUnapproved: true);
        Assert.Equal("30100", profile.CloseTargetAccountCode);
        Assert.Null(profile.DistributionAccountCode);
        Assert.Equal("Accumulated net worth", profile.DisplayNames["30100"]);
        Assert.False(profile.DisplayNames.ContainsKey("30800"));
    }
}
