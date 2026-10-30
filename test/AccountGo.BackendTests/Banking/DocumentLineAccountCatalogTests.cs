// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Sales;
using Services.Financial;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class DocumentLineAccountCatalogTests
{
    [Fact]
    public void ListBillLineAccounts_excludes_10120_and_summary_accounts()
    {
        using var harness = new BankingTestHarness();
        harness.SeedCashTree();
        harness.Repos.Accounts.Insert(new Account
        {
            Id = 200,
            CompanyId = 1,
            AccountCode = "10120",
            AccountName = "Accounts Receivable",
            AccountClassId = (int)AccountClasses.Assets,
            DrOrCrSide = DrOrCrSide.Dr,
            IsCash = false
        });
        harness.Repos.Accounts.Insert(new Account
        {
            Id = 201,
            CompanyId = 1,
            AccountCode = "50000",
            AccountName = "Expenses",
            AccountClassId = (int)AccountClasses.Expense,
            DrOrCrSide = DrOrCrSide.Dr,
            IsCash = false
        });
        harness.Repos.Accounts.Insert(new Account
        {
            Id = 202,
            CompanyId = 1,
            AccountCode = "50115",
            AccountName = "Subscriptions",
            AccountClassId = (int)AccountClasses.Expense,
            DrOrCrSide = DrOrCrSide.Dr,
            IsCash = false,
            ParentAccountId = 201
        });

        var catalog = new DocumentLineAccountCatalog(
            harness.Repos.Accounts,
            harness.Repos.Customers,
            new BankingTestHarness.FakeTenant(1));

        var list = catalog.ListBillLineAccounts();
        Assert.DoesNotContain(list, a => a.AccountCode == DocumentLineAccountCatalog.AccountsReceivableControlCode);
        Assert.DoesNotContain(list, a => a.Id == 201);
        Assert.Contains(list, a => a.Id == 202);

        Assert.Throws<InvalidOperationException>(() => catalog.EnsureBillLineAccount(200));
        Assert.Throws<InvalidOperationException>(() => catalog.EnsureBillLineAccount(201));
        catalog.EnsureBillLineAccount(202);
    }

    [Fact]
    public void ListBillLineAccounts_excludes_customer_ar_control_accounts()
    {
        using var harness = new BankingTestHarness();
        harness.Repos.Accounts.Insert(new Account
        {
            Id = 310,
            CompanyId = 1,
            AccountCode = "10125",
            AccountName = "AR-Client",
            AccountClassId = (int)AccountClasses.Assets,
            DrOrCrSide = DrOrCrSide.Dr
        });
        harness.Repos.Accounts.Insert(new Account
        {
            Id = 311,
            CompanyId = 1,
            AccountCode = "50100",
            AccountName = "Office",
            AccountClassId = (int)AccountClasses.Expense,
            DrOrCrSide = DrOrCrSide.Dr
        });
        harness.Repos.Customers.Insert(new Customer
        {
            Id = 1,
            CompanyId = 1,
            AccountsReceivableAccountId = 310
        });

        var catalog = new DocumentLineAccountCatalog(
            harness.Repos.Accounts,
            harness.Repos.Customers,
            new BankingTestHarness.FakeTenant(1));

        var list = catalog.ListBillLineAccounts();
        Assert.DoesNotContain(list, a => a.Id == 310);
        Assert.Contains(list, a => a.Id == 311);
    }
}
