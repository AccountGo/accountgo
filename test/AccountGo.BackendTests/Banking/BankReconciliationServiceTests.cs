// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Entitlements;
using Core.Tenancy;
using Dto.Banking;
using Moq;
using Services.Banking;
using Services.Financial;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class BankReconciliationServiceTests
{
    [Fact]
    public void Start_PreTicksOpeningBalance_BeginningBalanceIsZero()
    {
        using var harness = new Harness();
        harness.SeedCashTree();
        harness.SeedExpenseAccount();
        var bank = harness.CreateBankWithOpeningBalance(100m);

        var draft = harness.Reconciliations.Start(new StartBankReconciliationRequest
        {
            BankId = bank.Id,
            StatementEndDate = DateTime.UtcNow.Date,
            StatementEndingBalance = 100m
        }, "tester");

        Assert.Equal(0m, draft.BeginningBalance);
        Assert.Equal((int)BankReconciliationStatuses.Draft, draft.Status);
        Assert.Contains(draft.Deposits, d => d.IsOpeningBalance && d.Ticked);
        Assert.Equal(0m, draft.Difference);
    }

    [Fact]
    public void Finish_StampsLines_AndVoidRefusesUntilUndo()
    {
        using var harness = new Harness();
        harness.SeedCashTree();
        harness.SeedExpenseAccount();
        var bank = harness.CreateBankWithOpeningBalance(100m);
        var moneyOut = harness.Transactions.Create(new CreateBankTransactionRequest
        {
            Kind = (int)BankTransactionKinds.MoneyOut,
            BankId = bank.Id,
            ContraAccountId = harness.RequireAccount("50100").Id,
            Date = DateTime.UtcNow.Date,
            Amount = 25m
        });

        var draft = harness.Reconciliations.Start(new StartBankReconciliationRequest
        {
            BankId = bank.Id,
            StatementEndDate = DateTime.UtcNow.Date,
            StatementEndingBalance = 75m
        }, "tester");

        var moneyOutLine = harness.Repos.GlLines.Table.Single(l =>
            l.GeneralLedgerHeaderId == moneyOut.GeneralLedgerHeaderId
            && l.AccountId == bank.AccountId);
        draft = harness.Reconciliations.UpdateItems(draft.Id, new UpdateBankReconciliationItemsRequest
        {
            Add = new[] { moneyOutLine.Id },
            Version = draft.Version
        });
        Assert.Equal(0m, draft.Difference);

        var finished = harness.Reconciliations.Finish(draft.Id, new FinishBankReconciliationRequest
        {
            Version = draft.Version
        }, "tester");
        Assert.Equal((int)BankReconciliationStatuses.Finished, finished.Status);
        Assert.Equal(bank.Id, harness.Banks.Get(bank.Id, includeFullNumbers: false).Id);
        Assert.Equal(finished.StatementEndDate.Date,
            harness.Banks.Get(bank.Id, includeFullNumbers: false).ReconciledThrough!.Value.Date);

        var stamped = harness.Repos.GlLines.Table.Where(l => l.BankReconciliationId == finished.Id).ToList();
        Assert.Equal(2, stamped.Count);

        var voidEx = Assert.Throws<ReconciledTransactionException>(() =>
            harness.Transactions.Void(moneyOut.Id, new VoidBankTransactionRequest()));
        Assert.Equal(ReconciledTransactionException.ErrorCode, ReconciledTransactionException.ErrorCode);
        Assert.Contains("reconciled", voidEx.Message, StringComparison.OrdinalIgnoreCase);

        var undone = harness.Reconciliations.Undo(finished.Id, new UndoBankReconciliationRequest
        {
            Reason = "Correction"
        }, "tester");
        Assert.Equal((int)BankReconciliationStatuses.Undone, undone.Status);
        Assert.All(
            harness.Repos.GlLines.Table.Where(l => stamped.Select(s => s.Id).Contains(l.Id)),
            l => Assert.Null(l.BankReconciliationId));

        var voided = harness.Transactions.Void(moneyOut.Id, new VoidBankTransactionRequest());
        Assert.True(voided.IsVoid);
    }

    [Fact]
    public void Start_SecondDraft_ConflictsWithExisting()
    {
        using var harness = new Harness();
        harness.SeedCashTree();
        var bank = harness.CreateBank("Checking");

        harness.Reconciliations.Start(new StartBankReconciliationRequest
        {
            BankId = bank.Id,
            StatementEndDate = DateTime.UtcNow.Date,
            StatementEndingBalance = 0m
        }, "tester");

        var ex = Assert.Throws<BankReconciliationConflictException>(() =>
            harness.Reconciliations.Start(new StartBankReconciliationRequest
            {
                BankId = bank.Id,
                StatementEndDate = DateTime.UtcNow.Date.AddDays(1),
                StatementEndingBalance = 0m
            }, "tester"));
        Assert.Equal(BankReconciliationConflictException.DraftExistsCode, ex.Code);
    }

    [Fact]
    public void Undo_RefusesWhileDraftExists()
    {
        using var harness = new Harness();
        harness.SeedCashTree();
        harness.SeedExpenseAccount();
        var bank = harness.CreateBankWithOpeningBalance(50m);

        var first = harness.Reconciliations.Start(new StartBankReconciliationRequest
        {
            BankId = bank.Id,
            StatementEndDate = new DateTime(2024, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            StatementEndingBalance = 50m
        }, "tester");
        first = harness.Reconciliations.Finish(first.Id, new FinishBankReconciliationRequest
        {
            Version = first.Version
        }, "tester");

        harness.Reconciliations.Start(new StartBankReconciliationRequest
        {
            BankId = bank.Id,
            StatementEndDate = new DateTime(2024, 2, 29, 0, 0, 0, DateTimeKind.Utc),
            StatementEndingBalance = 50m
        }, "tester");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            harness.Reconciliations.Undo(first.Id, new UndoBankReconciliationRequest
            {
                Reason = "Should refuse"
            }, "tester"));
        Assert.Contains("Discard or finish the draft first", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Start_PreTicksOnlyCurrentOpeningBalance_NotPriorVoidedOb()
    {
        using var harness = new Harness();
        harness.SeedCashTree();
        var bank = harness.CreateBankWithOpeningBalance(100m);
        var firstObHeaderId = harness.Repos.Banks.GetById(bank.Id).OpeningBalanceGeneralLedgerHeaderId;
        Assert.NotNull(firstObHeaderId);

        bank = harness.Banks.Update(bank.Id, new UpdateBankAccountRequest
        {
            Name = bank.Name,
            Type = bank.Type,
            OpeningBalance = 150m,
            OpeningBalanceDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        }, includeFullNumbers: false);

        var currentObHeaderId = harness.Repos.Banks.GetById(bank.Id).OpeningBalanceGeneralLedgerHeaderId;
        Assert.NotNull(currentObHeaderId);
        Assert.NotEqual(firstObHeaderId, currentObHeaderId);

        var draft = harness.Reconciliations.Start(new StartBankReconciliationRequest
        {
            BankId = bank.Id,
            StatementEndDate = DateTime.UtcNow.Date,
            StatementEndingBalance = 150m
        }, "tester");

        var openingDeposit = Assert.Single(draft.Deposits, d => d.IsOpeningBalance);
        Assert.Equal(currentObHeaderId, openingDeposit.HeaderId);
        Assert.DoesNotContain(draft.Deposits, d => d.HeaderId == firstObHeaderId);
        Assert.DoesNotContain(draft.Payments, p => p.HeaderId == firstObHeaderId);
        Assert.Equal(0m, draft.Difference);

        // Prior OB + its reversal are auto-cleared (items exist, hidden from UI).
        var tickedHeaderIds = harness.Repos.ReconciliationItems.Table
            .Where(i => i.BankReconciliationId == draft.Id)
            .Select(i => harness.Repos.GlLines.GetById(i.GeneralLedgerLineId).GeneralLedgerHeaderId)
            .ToHashSet();
        Assert.Contains(firstObHeaderId!.Value, tickedHeaderIds);
        Assert.Contains(currentObHeaderId!.Value, tickedHeaderIds);
        Assert.Contains(
            harness.Repos.GlHeaders.Table.Where(h => h.ReversedGeneralLedgerHeaderId == firstObHeaderId).Select(h => h.Id),
            id => tickedHeaderIds.Contains(id));
    }

    [Fact]
    public void Start_AutoClearsVoidAndReversalPair_HiddenFromCandidates()
    {
        using var harness = new Harness();
        harness.SeedCashTree();
        harness.SeedExpenseAccount();
        var bank = harness.CreateBank("Ops");
        var moneyOut = harness.Transactions.Create(new CreateBankTransactionRequest
        {
            Kind = (int)BankTransactionKinds.MoneyOut,
            BankId = bank.Id,
            ContraAccountId = harness.RequireAccount("50100").Id,
            Date = DateTime.UtcNow.Date,
            Amount = 40m
        });
        harness.Transactions.Void(moneyOut.Id, new VoidBankTransactionRequest
        {
            VoidDate = DateTime.UtcNow.Date
        });

        var draft = harness.Reconciliations.Start(new StartBankReconciliationRequest
        {
            BankId = bank.Id,
            StatementEndDate = DateTime.UtcNow.Date,
            StatementEndingBalance = 0m
        }, "tester");

        Assert.Empty(draft.Deposits);
        Assert.Empty(draft.Payments);
        Assert.Equal(0m, draft.Difference);

        var originalHeaderId = moneyOut.GeneralLedgerHeaderId!.Value;
        var voidHeaderId = harness.Repos.BankTransactions.GetById(moneyOut.Id).VoidGeneralLedgerHeaderId;
        Assert.NotNull(voidHeaderId);

        var tickedHeaderIds = harness.Repos.ReconciliationItems.Table
            .Where(i => i.BankReconciliationId == draft.Id)
            .Select(i => harness.Repos.GlLines.GetById(i.GeneralLedgerLineId).GeneralLedgerHeaderId)
            .ToHashSet();
        Assert.Contains(originalHeaderId, tickedHeaderIds);
        Assert.Contains(voidHeaderId.Value, tickedHeaderIds);

        var finished = harness.Reconciliations.Finish(draft.Id, new FinishBankReconciliationRequest
        {
            Version = draft.Version
        }, "tester");
        Assert.Equal((int)BankReconciliationStatuses.Finished, finished.Status);
        Assert.Empty(finished.Deposits);
        Assert.Empty(finished.Payments);
        Assert.All(
            harness.Repos.GlLines.Table.Where(l =>
                l.AccountId == bank.AccountId
                && (l.GeneralLedgerHeaderId == originalHeaderId || l.GeneralLedgerHeaderId == voidHeaderId)),
            l => Assert.Equal(finished.Id, l.BankReconciliationId));
    }

    [Fact]
    public void MoneyOut_RefusesCustomerArAndVendorApContra()
    {
        using var harness = new Harness();
        harness.SeedCashTree();
        var bank = harness.CreateBank("Ops");
        var customerAr = harness.AddAccount("10121", "Customer AR", isCash: false, id: 30);
        var vendorAp = harness.AddAccount("20111", "Vendor AP", isCash: false, id: 31);
        harness.Repos.Customers.Insert(new Customer
        {
            Id = 1,
            CompanyId = 1,
            AccountsReceivableAccountId = customerAr.Id
        });
        harness.Repos.Vendors.Insert(new Vendor
        {
            Id = 1,
            CompanyId = 1,
            AccountsPayableAccountId = vendorAp.Id
        });

        var customerEx = Assert.Throws<InvalidOperationException>(() =>
            harness.Transactions.Create(new CreateBankTransactionRequest
            {
                Kind = (int)BankTransactionKinds.MoneyOut,
                BankId = bank.Id,
                ContraAccountId = customerAr.Id,
                Date = DateTime.UtcNow.Date,
                Amount = 10m
            }));
        Assert.Contains("Customer AR", customerEx.Message, StringComparison.OrdinalIgnoreCase);

        var vendorEx = Assert.Throws<InvalidOperationException>(() =>
            harness.Transactions.Create(new CreateBankTransactionRequest
            {
                Kind = (int)BankTransactionKinds.MoneyOut,
                BankId = bank.Id,
                ContraAccountId = vendorAp.Id,
                Date = DateTime.UtcNow.Date,
                Amount = 10m
            }));
        Assert.Contains("vendor AP", vendorEx.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Harness : IDisposable
    {
        public InMemoryRepos Repos { get; } = new();
        public BankAccountService Banks { get; }
        public BankTransactionService Transactions { get; }
        public BankReconciliationService Reconciliations { get; }
        public Mock<IFinancialService> Financial { get; }

        public Harness()
        {
            var tenant = new FakeTenant(1);
            var entitlements = new Mock<IEntitlementService>();
            entitlements.Setup(e => e.EnsureCanCreate(It.IsAny<MeteredResource>(), It.IsAny<int>()));
            entitlements.Setup(e => e.Evaluate(It.IsAny<int>(), It.IsAny<EntitlementAction>()))
                .Returns(new EntitlementDecision(true, null, null));
            Financial = new Mock<IFinancialService>(MockBehavior.Loose);
            SetupFinancial();

            Banks = new BankAccountService(
                Repos.Banks,
                Repos.BankTransactions,
                Repos.Reconciliations,
                Repos.Accounts,
                Repos.GlLines,
                Repos.GlHeaders,
                Repos.Settings,
                Repos.FiscalYears,
                Repos.Receipts,
                Repos.Allocations,
                Repos.SalesInvoices,
                Repos.VendorPayments,
                Repos.PurchaseInvoices,
                Repos.Journals,
                Repos.Customers,
                Repos.Vendors,
                Repos.Parties,
                Financial.Object,
                entitlements.Object,
                tenant);

            Transactions = new BankTransactionService(
                Repos.Sequences,
                Repos.GlSettings,
                Repos.PaymentTerms,
                Repos.Banks,
                Repos.BankTransactions,
                Repos.Accounts,
                Repos.Parties,
                Repos.Customers,
                Repos.Vendors,
                Financial.Object,
                tenant);

            Reconciliations = new BankReconciliationService(
                Repos.Reconciliations,
                Repos.ReconciliationItems,
                Repos.Banks,
                Repos.GlLines,
                Repos.GlHeaders,
                Transactions,
                tenant);

            Repos.Settings.Insert(new CompanySetting
            {
                CompanyId = 1,
                CoaTemplateId = CoaTemplates.Legacy,
                BooksStartDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
            Repos.FiscalYears.Insert(new FinancialYear
            {
                Id = 1,
                CompanyId = 1,
                StartDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2099, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                IsActive = true
            });
        }

        public void Dispose() { }

        public void SeedCashTree()
        {
            AddAccount("10110", "Cash", null, isCash: true, id: 10);
            AddAccount("10111", "Checking", "10110", isCash: false, id: 11);
            AddAccount("10112", "Savings", "10110", isCash: false, id: 12);
            AddAccount("30700", "Opening Balance Equity", null, isCash: false, id: 50);
        }

        public void SeedExpenseAccount() =>
            AddAccount("50100", "Office expense", null, isCash: false, id: 61);

        public Account AddAccount(string code, string name, bool isCash, int id) =>
            AddAccount(code, name, null, isCash, id);

        public Account RequireAccount(string code) =>
            Repos.Accounts.Table.Single(a => a.AccountCode == code);

        public BankAccountDto CreateBank(string name) =>
            Banks.Create(new CreateBankAccountRequest { Name = name, Type = 1 }, includeFullNumbers: false);

        public BankAccountDto CreateBankWithOpeningBalance(decimal amount)
        {
            var bank = Banks.Create(new CreateBankAccountRequest
            {
                Name = "Checking",
                Type = 1,
                OpeningBalance = amount,
                OpeningBalanceDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }, includeFullNumbers: false);
            return bank;
        }

        private void SetupFinancial()
        {
            Financial
                .Setup(f => f.GetAccountByAccountCode(It.IsAny<string>()))
                .Returns((string code) => Repos.Accounts.Table.FirstOrDefault(a => a.AccountCode == code)!);
            Financial
                .Setup(f => f.CreateGeneralLedgerHeader(It.IsAny<DocumentTypes>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>()))
                .Returns((DocumentTypes type, DateTime date, string desc, int? sourceId, int? rev) =>
                    new GeneralLedgerHeader
                    {
                        DocumentType = type,
                        Date = date,
                        Description = desc,
                        SourceDocumentId = sourceId,
                        ReversedGeneralLedgerHeaderId = rev
                    });
            Financial
                .Setup(f => f.CreateGeneralLedgerLine(It.IsAny<DrOrCrSide>(), It.IsAny<int>(), It.IsAny<decimal>()))
                .Returns((DrOrCrSide side, int accountId, decimal amount) =>
                    new GeneralLedgerLine { DrCr = side, AccountId = accountId, Amount = amount });
            Financial
                .Setup(f => f.SaveGeneralLedgerEntry(It.IsAny<GeneralLedgerHeader>()))
                .Callback<GeneralLedgerHeader>(h =>
                {
                    if (h.Id == 0)
                        h.Id = Repos.NextGlHeaderId();
                    Repos.GlHeaders.Insert(h);
                    foreach (var line in h.GeneralLedgerLines)
                    {
                        if (line.Id == 0)
                            line.Id = Repos.NextGlLineId();
                        line.GeneralLedgerHeaderId = h.Id;
                        line.CompanyId = 1;
                        Repos.GlLines.Insert(line);
                    }
                });
            Financial
                .Setup(f => f.EnsureNotReconciled(It.IsAny<int>()))
                .Callback<int>(ThrowIfReconciled);
            Financial
                .Setup(f => f.CreateReversingEntry(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>()))
                .Returns((int headerId, string desc, DateTime reversalDate) =>
                {
                    ThrowIfReconciled(headerId);
                    var original = Repos.GlHeaders.GetById(headerId);
                    var reversal = new GeneralLedgerHeader
                    {
                        DocumentType = original.DocumentType,
                        Date = reversalDate.Date,
                        Description = desc,
                        SourceDocumentId = original.SourceDocumentId,
                        ReversedGeneralLedgerHeaderId = headerId
                    };
                    foreach (var line in Repos.GlLines.Table.Where(l => l.GeneralLedgerHeaderId == headerId))
                        reversal.GeneralLedgerLines.Add(new GeneralLedgerLine
                        {
                            DrCr = line.DrCr == DrOrCrSide.Dr ? DrOrCrSide.Cr : DrOrCrSide.Dr,
                            AccountId = line.AccountId,
                            Amount = line.Amount
                        });
                    return reversal;
                });
            Financial
                .Setup(f => f.ValidateGeneralLedgerEntry(It.IsAny<GeneralLedgerHeader>()))
                .Returns(true);
        }

        private void ThrowIfReconciled(int generalLedgerHeaderId)
        {
            var finished = (
                from line in Repos.GlLines.Table
                where line.GeneralLedgerHeaderId == generalLedgerHeaderId && line.BankReconciliationId != null
                join recon in Repos.Reconciliations.Table on line.BankReconciliationId equals recon.Id
                where recon.Status == BankReconciliationStatuses.Finished
                select recon
            ).FirstOrDefault();
            if (finished == null)
                return;
            var bankName = Repos.Banks.GetById(finished.BankId)?.Name ?? "bank";
            throw new ReconciledTransactionException(bankName, finished.StatementEndDate);
        }

        private Account AddAccount(string code, string name, string? parentCode, bool isCash, int id)
        {
            int? parentId = null;
            if (parentCode != null)
                parentId = Repos.Accounts.Table.Single(a => a.AccountCode == parentCode).Id;
            var account = new Account
            {
                Id = id,
                CompanyId = 1,
                AccountCode = code,
                AccountName = name,
                ParentAccountId = parentId,
                IsCash = isCash,
                AccountClassId = 1,
                DrOrCrSide = DrOrCrSide.Dr
            };
            Repos.Accounts.Insert(account);
            return account;
        }

        private sealed class FakeTenant : ITenantContext
        {
            public FakeTenant(int companyId) => CompanyId = companyId;
            public bool IsResolved => true;
            public int? CompanyId { get; }
            public string? CompanySlug => "test";
            public CompanyMemberRole? Role => CompanyMemberRole.Owner;
            public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();
            public int RequireCompanyId() => CompanyId!.Value;
            public IDisposable Impersonate(int companyId, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null) =>
                throw new NotSupportedException();
        }

        public sealed class InMemoryRepos
        {
            private int _glHeaderId = 100;
            private int _glLineId = 100;
            public MemoryRepo<Account> Accounts { get; } = new();
            public MemoryRepo<Bank> Banks { get; } = new();
            public MemoryRepo<BankTransaction> BankTransactions { get; } = new();
            public MemoryRepo<BankReconciliation> Reconciliations { get; } = new();
            public MemoryRepo<BankReconciliationItem> ReconciliationItems { get; } = new();
            public MemoryRepo<GeneralLedgerLine> GlLines { get; } = new();
            public MemoryRepo<GeneralLedgerHeader> GlHeaders { get; } = new();
            public MemoryRepo<CompanySetting> Settings { get; } = new();
            public MemoryRepo<FinancialYear> FiscalYears { get; } = new();
            public MemoryRepo<SalesReceiptHeader> Receipts { get; } = new();
            public MemoryRepo<CustomerAllocation> Allocations { get; } = new();
            public MemoryRepo<SalesInvoiceHeader> SalesInvoices { get; } = new();
            public MemoryRepo<VendorPayment> VendorPayments { get; } = new();
            public MemoryRepo<PurchaseInvoiceHeader> PurchaseInvoices { get; } = new();
            public MemoryRepo<JournalEntryHeader> Journals { get; } = new();
            public MemoryRepo<Customer> Customers { get; } = new();
            public MemoryRepo<Vendor> Vendors { get; } = new();
            public MemoryRepo<Party> Parties { get; } = new();
            public MemoryRepo<SequenceNumber> Sequences { get; } = new();
            public MemoryRepo<GeneralLedgerSetting> GlSettings { get; } = new();
            public MemoryRepo<PaymentTerm> PaymentTerms { get; } = new();
            public int NextGlHeaderId() => ++_glHeaderId;
            public int NextGlLineId() => ++_glLineId;
        }

        public sealed class MemoryRepo<T> : IRepository<T> where T : BaseEntity
        {
            private readonly List<T> _items = new();
            private int _autoId;
            public IQueryable<T> Table => _items.AsQueryable();
            public T GetById(object id)
            {
                var key = Convert.ToInt32(id);
                return _items.FirstOrDefault(i => i.Id == key)!;
            }
            public void Insert(T entity)
            {
                if (entity.Id == 0)
                {
                    var max = _items.Count == 0 ? 0 : _items.Max(i => i.Id);
                    entity.Id = Math.Max(max, _autoId) + 1;
                    _autoId = entity.Id;
                }
                if (entity is ICompanyOwned owned && owned.CompanyId == 0)
                    owned.CompanyId = 1;
                _items.Add(entity);
            }
            public System.Threading.Tasks.Task InsertAsync(T entity) { Insert(entity); return System.Threading.Tasks.Task.CompletedTask; }
            public void Update(T entity) { }
            public System.Threading.Tasks.Task UpdateAsync(T entity) { Update(entity); return System.Threading.Tasks.Task.CompletedTask; }
            public void Delete(T entity) => _items.Remove(entity);
            public System.Threading.Tasks.Task DeleteAsync(T entity) { Delete(entity); return System.Threading.Tasks.Task.CompletedTask; }
            public IQueryable<T> GetAllIncluding(params System.Linq.Expressions.Expression<Func<T, object>>[] includeProperties) => Table;
        }
    }
}
