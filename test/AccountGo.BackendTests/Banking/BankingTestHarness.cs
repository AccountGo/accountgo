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
using Core.Domain.TaxSystem;
using Core.Entitlements;
using Core.Tenancy;
using Dto.Banking;
using Moq;
using Services.Banking;
using Services.Financial;

namespace AccountGo.BackendTests.Banking;

/// <summary>
/// In-memory harness shared by the Phase 3 banking tests (same pattern as the 2A/2B harnesses).
/// Repositories do not filter by tenant, so services check <c>CompanyId</c> explicitly.
/// </summary>
internal sealed class BankingTestHarness : IDisposable
{
    public BankingInMemoryRepos Repos { get; } = new();
    public Mock<IFinancialService> Financial { get; }
    public Mock<IEntitlementService> Entitlements { get; }

    public BankAccountService Banks { get; }
    public BankTransactionService Transactions { get; }
    public BankReconciliationService Reconciliations { get; }
    public BankStatementImportService Statements { get; }

    /// <summary>The real guard over the in-memory repositories; the mocked financial service calls it like FinancialService does.</summary>
    public ReconciledPeriodGuard Guard { get; }

    public BankingTestHarness()
    {
        Entitlements = new Mock<IEntitlementService>();
        Entitlements.Setup(e => e.EnsureCanCreate(It.IsAny<MeteredResource>(), It.IsAny<int>()));
        Entitlements.Setup(e => e.Evaluate(It.IsAny<int>(), It.IsAny<EntitlementAction>()))
            .Returns(new EntitlementDecision(true, null, null));
        Guard = new ReconciledPeriodGuard(Repos.Banks, Repos.Reconciliations, Repos.BankTransactions, new FakeTenant(companyId: 1));
        Financial = new Mock<IFinancialService>(MockBehavior.Loose);
        SetupFinancial();

        var services = BuildServices(companyId: 1);
        Banks = services.Banks;
        Transactions = services.Transactions;
        Reconciliations = services.Reconciliations;
        Statements = services.Statements;
        Repos.Companies.Insert(new Company { Id = 1, Name = "Test Co", ShortName = "Test" });
        Repos.Settings.Insert(new CompanySetting
        {
            CompanyId = 1,
            CoaTemplateId = CoaTemplates.Legacy,
            Currency = "USD",
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

    public sealed record ServiceSet(
        BankAccountService Banks,
        BankTransactionService Transactions,
        BankReconciliationService Reconciliations,
        BankStatementImportService Statements);

    /// <summary>Services bound to a different tenant over the same repositories (isolation tests).</summary>
    public ServiceSet BuildServices(int companyId)
    {
        var tenant = new FakeTenant(companyId);

        var banks = new BankAccountService(
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
            Entitlements.Object,
            tenant);

        var transactions = new BankTransactionService(
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

        var reconciliations = new BankReconciliationService(
            Repos.Reconciliations,
            Repos.ReconciliationItems,
            Repos.Banks,
            Repos.GlLines,
            Repos.GlHeaders,
            Repos.Companies,
            Repos.Settings,
            transactions,
            tenant);

        var statements = new BankStatementImportService(
            Repos.StatementImports,
            Repos.StatementLines,
            Repos.ColumnMappings,
            Repos.Banks,
            Repos.GlLines,
            Repos.GlHeaders,
            Repos.Settings,
            transactions,
            tenant);

        return new ServiceSet(banks, transactions, reconciliations, statements);
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

    public void SeedFeeAndInterestAccounts()
    {
        AddAccount("50121", "Bank Charges", null, isCash: false, id: 60);
        AddAccount("40300", "Interest Income", null, isCash: false, id: 62);
    }

    public Account RequireAccount(string code) =>
        Repos.Accounts.Table.Single(a => a.AccountCode == code);

    public BankAccountDto CreateBank(string name) =>
        Banks.Create(new CreateBankAccountRequest { Name = name, Type = 1 }, includeFullNumbers: false);

    /// <summary>Posts a single-sided GL line on <paramref name="accountId"/> dated <paramref name="date"/>.</summary>
    public GeneralLedgerLine Post(int accountId, DrOrCrSide side, decimal amount, DateTime date, string description = "seed")
    {
        var header = new GeneralLedgerHeader
        {
            Id = Repos.NextGlHeaderId(),
            CompanyId = 1,
            DocumentType = DocumentTypes.JournalEntry,
            Date = date.Date,
            Description = description
        };
        Repos.GlHeaders.Insert(header);
        var line = new GeneralLedgerLine
        {
            Id = Repos.NextGlLineId(),
            CompanyId = 1,
            GeneralLedgerHeaderId = header.Id,
            AccountId = accountId,
            DrCr = side,
            Amount = amount
        };
        Repos.GlLines.Insert(line);
        return line;
    }

    public decimal AccountBalance(int accountId) =>
        Repos.GlLines.Table.Where(l => l.AccountId == accountId)
            .Sum(l => l.DrCr == DrOrCrSide.Dr ? l.Amount : -l.Amount);

    public static DateTime Utc(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

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
                Guard.EnsurePostingAllowed(h);
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

    private void AddAccount(string code, string name, string? parentCode, bool isCash, int id)
    {
        int? parentId = null;
        if (parentCode != null)
            parentId = Repos.Accounts.Table.Single(a => a.AccountCode == parentCode).Id;
        Repos.Accounts.Insert(new Account
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

    internal sealed class FakeTenant : ITenantContext
    {
        public FakeTenant(int companyId, IReadOnlySet<string>? permissions = null)
        {
            CompanyId = companyId;
            Permissions = permissions ?? new HashSet<string>();
        }
        public bool IsResolved => true;
        public int? CompanyId { get; }
        public string? CompanySlug => "test";
        public CompanyMemberRole? Role => CompanyMemberRole.Owner;
        public IReadOnlySet<string> Permissions { get; }
        public int RequireCompanyId() => CompanyId!.Value;
        public IDisposable Impersonate(int companyId, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null) =>
            throw new NotSupportedException();
    }
}

internal sealed class BankingInMemoryRepos
{
    private int _glHeaderId = 100;
    private int _glLineId = 100;
    public BankingMemoryRepo<Account> Accounts { get; } = new();
    public BankingMemoryRepo<Bank> Banks { get; } = new();
    public BankingMemoryRepo<BankTransaction> BankTransactions { get; } = new();
    public BankingMemoryRepo<BankReconciliation> Reconciliations { get; } = new();
    public BankingMemoryRepo<BankReconciliationItem> ReconciliationItems { get; } = new();
    public BankingMemoryRepo<BankStatementImport> StatementImports { get; } = new();
    public BankingMemoryRepo<BankStatementLine> StatementLines { get; } = new();
    public BankingMemoryRepo<BankStatementColumnMapping> ColumnMappings { get; } = new();
    public BankingMemoryRepo<BankStatementDescriptionRule> StatementDescriptionRules { get; } = new();
    public BankingMemoryRepo<GeneralLedgerLine> GlLines { get; } = new();
    public BankingMemoryRepo<GeneralLedgerHeader> GlHeaders { get; } = new();
    public BankingMemoryRepo<Company> Companies { get; } = new();
    public BankingMemoryRepo<CompanySetting> Settings { get; } = new();
    public BankingMemoryRepo<FinancialYear> FiscalYears { get; } = new();
    public BankingMemoryRepo<SalesReceiptHeader> Receipts { get; } = new();
    public BankingMemoryRepo<CustomerAllocation> Allocations { get; } = new();
    public BankingMemoryRepo<SalesInvoiceHeader> SalesInvoices { get; } = new();
    public BankingMemoryRepo<VendorPayment> VendorPayments { get; } = new();
    public BankingMemoryRepo<PurchaseInvoiceHeader> PurchaseInvoices { get; } = new();
    public BankingMemoryRepo<JournalEntryHeader> Journals { get; } = new();
    public BankingMemoryRepo<Customer> Customers { get; } = new();
    public BankingMemoryRepo<Vendor> Vendors { get; } = new();
    public BankingMemoryRepo<Tax> Taxes { get; } = new();
    public BankingMemoryRepo<Party> Parties { get; } = new();
    public BankingMemoryRepo<SequenceNumber> Sequences { get; } = new();
    public BankingMemoryRepo<GeneralLedgerSetting> GlSettings { get; } = new();
    public BankingMemoryRepo<PaymentTerm> PaymentTerms { get; } = new();
    public int NextGlHeaderId() => ++_glHeaderId;
    public int NextGlLineId() => ++_glLineId;
}

internal sealed class BankingMemoryRepo<T> : IRepository<T> where T : BaseEntity
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
