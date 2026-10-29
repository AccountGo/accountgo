// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
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

public class BankTransactionServiceTests
{
    [Fact]
    public void Transfer_MovesAmountBetweenBanks_AndTrialBalanceUnchanged()
    {
        using var harness = new Harness();
        harness.SeedCashTree();
        var checking = harness.CreateBank("Checking");
        var savings = harness.CreateBank("Savings");
        harness.PostDebit(checking.AccountId!.Value, 500m);

        var before = harness.TrialBalance();
        var tx = harness.Transactions.Create(new CreateBankTransactionRequest
        {
            Kind = (int)BankTransactionKinds.Transfer,
            BankId = checking.Id,
            ToBankId = savings.Id,
            Date = DateTime.UtcNow.Date,
            Amount = 100m
        });

        Assert.Equal((int)BankTransactionKinds.Transfer, tx.Kind);
        Assert.False(tx.IsVoid);
        Assert.Equal(-100m, harness.AccountBalance(checking.AccountId!.Value) - 500m);
        Assert.Equal(100m, harness.AccountBalance(savings.AccountId!.Value));
        Assert.Equal(before, harness.TrialBalance());
    }

    [Fact]
    public void MoneyOut_RefusesContraEqualToBankAccount()
    {
        using var harness = new Harness();
        harness.SeedCashTree();
        var bank = harness.CreateBank("Ops");
        var ex = Assert.Throws<InvalidOperationException>(() =>
            harness.Transactions.Create(new CreateBankTransactionRequest
            {
                Kind = (int)BankTransactionKinds.MoneyOut,
                BankId = bank.Id,
                ContraAccountId = bank.AccountId,
                Date = DateTime.UtcNow.Date,
                Amount = 10m
            }));
        Assert.Contains("same as the bank account", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Void_ReversesLedgerAndMarksVoid()
    {
        using var harness = new Harness();
        harness.SeedCashTree();
        harness.SeedFeeAccount();
        var bank = harness.CreateBank("Ops");
        harness.PostDebit(bank.AccountId!.Value, 50m);

        var created = harness.Transactions.Create(new CreateBankTransactionRequest
        {
            Kind = (int)BankTransactionKinds.Fee,
            BankId = bank.Id,
            Date = DateTime.UtcNow.Date,
            Amount = 5m
        });
        Assert.Equal(45m, harness.AccountBalance(bank.AccountId!.Value));

        var voided = harness.Transactions.Void(created.Id, new VoidBankTransactionRequest
        {
            VoidDate = DateTime.UtcNow.Date,
            Reason = "Posted twice"
        });
        Assert.True(voided.IsVoid);
        Assert.NotNull(voided.VoidGeneralLedgerHeaderId);
        Assert.Equal(50m, harness.AccountBalance(bank.AccountId!.Value));
    }

    private sealed class Harness : IDisposable
    {
        private readonly InMemoryRepos _repos = new();
        public BankAccountService Banks { get; }
        public BankTransactionService Transactions { get; }
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
                _repos.Banks,
                _repos.BankTransactions,
                _repos.Accounts,
                _repos.GlLines,
                _repos.GlHeaders,
                _repos.Settings,
                _repos.FiscalYears,
                _repos.Receipts,
                _repos.Allocations,
                _repos.SalesInvoices,
                _repos.VendorPayments,
                _repos.PurchaseInvoices,
                _repos.Journals,
                _repos.Customers,
                _repos.Vendors,
                _repos.Parties,
                Financial.Object,
                entitlements.Object,
                tenant);

            Transactions = new BankTransactionService(
                _repos.Sequences,
                _repos.GlSettings,
                _repos.PaymentTerms,
                _repos.Banks,
                _repos.BankTransactions,
                _repos.Accounts,
                _repos.Parties,
                Financial.Object,
                tenant);

            _repos.Settings.Insert(new CompanySetting
            {
                CompanyId = 1,
                CoaTemplateId = CoaTemplates.Legacy,
                BooksStartDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
            _repos.FiscalYears.Insert(new FinancialYear
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
            AddAccount("10113", "Cash in Hand", "10110", isCash: false, id: 13);
            AddAccount("30700", "Opening Balance Equity", null, isCash: false, id: 50);
        }

        public void SeedFeeAccount() =>
            AddAccount("50121", "Bank Charges", null, isCash: false, id: 60);

        public BankAccountDto CreateBank(string name) =>
            Banks.Create(new CreateBankAccountRequest { Name = name, Type = 1 }, includeFullNumbers: false);

        public void PostDebit(int accountId, decimal amount)
        {
            var header = new GeneralLedgerHeader
            {
                Id = _repos.NextGlHeaderId(),
                CompanyId = 1,
                DocumentType = DocumentTypes.JournalEntry,
                Date = DateTime.UtcNow.Date,
                Description = "seed"
            };
            _repos.GlHeaders.Insert(header);
            _repos.GlLines.Insert(new GeneralLedgerLine
            {
                Id = _repos.NextGlLineId(),
                CompanyId = 1,
                GeneralLedgerHeaderId = header.Id,
                AccountId = accountId,
                DrCr = DrOrCrSide.Dr,
                Amount = amount
            });
        }

        public decimal AccountBalance(int accountId) =>
            _repos.GlLines.Table.Where(l => l.AccountId == accountId)
                .Sum(l => l.DrCr == DrOrCrSide.Dr ? l.Amount : -l.Amount);

        public decimal TrialBalance() =>
            _repos.GlLines.Table.Sum(l => l.DrCr == DrOrCrSide.Dr ? l.Amount : -l.Amount);

        private void SetupFinancial()
        {
            Financial
                .Setup(f => f.GetAccountByAccountCode(It.IsAny<string>()))
                .Returns((string code) => _repos.Accounts.Table.FirstOrDefault(a => a.AccountCode == code)!);
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
                        h.Id = _repos.NextGlHeaderId();
                    _repos.GlHeaders.Insert(h);
                    foreach (var line in h.GeneralLedgerLines)
                    {
                        if (line.Id == 0)
                            line.Id = _repos.NextGlLineId();
                        line.GeneralLedgerHeaderId = h.Id;
                        line.CompanyId = 1;
                        _repos.GlLines.Insert(line);
                    }
                });
            Financial
                .Setup(f => f.CreateReversingEntry(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>()))
                .Returns((int headerId, string desc, DateTime reversalDate) =>
                {
                    var original = _repos.GlHeaders.GetById(headerId);
                    var reversal = new GeneralLedgerHeader
                    {
                        DocumentType = original.DocumentType,
                        Date = reversalDate.Date,
                        Description = desc,
                        SourceDocumentId = original.SourceDocumentId,
                        ReversedGeneralLedgerHeaderId = headerId
                    };
                    foreach (var line in _repos.GlLines.Table.Where(l => l.GeneralLedgerHeaderId == headerId))
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

        private void AddAccount(string code, string name, string? parentCode, bool isCash, int id)
        {
            int? parentId = null;
            if (parentCode != null)
                parentId = _repos.Accounts.Table.Single(a => a.AccountCode == parentCode).Id;
            _repos.Accounts.Insert(new Account
            {
                Id = id,
                CompanyId = 1,
                AccountCode = code,
                AccountName = name,
                ParentAccountId = parentId,
                IsCash = isCash,
                AccountClassId = 1,
                DrOrCrSide = DrOrCrSide.Dr
            });
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

        private sealed class InMemoryRepos
        {
            private int _glHeaderId = 100;
            private int _glLineId = 100;
            public MemoryRepo<Account> Accounts { get; } = new();
            public MemoryRepo<Bank> Banks { get; } = new();
            public MemoryRepo<BankTransaction> BankTransactions { get; } = new();
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

        private sealed class MemoryRepo<T> : IRepository<T> where T : BaseEntity
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
