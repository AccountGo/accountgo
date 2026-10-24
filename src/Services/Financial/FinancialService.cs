//-----------------------------------------------------------------------
// <copyright file="FinancialService.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.TaxSystem;
using Services.Inventory;
using Services.TaxSystem;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Services.Financial
{
    public partial class FinancialService : BaseService, IFinancialService
    {
        private readonly IInventoryService _inventoryService;
        private readonly ITaxService _taxService;

        private readonly IRepository<GeneralLedgerHeader> _generalLedgerRepository;
        private readonly IRepository<Account> _accountRepo;
        private readonly IRepository<AccountClass> _accountClassRepo;
        private readonly IRepository<Tax> _taxRepo;
        private readonly IRepository<JournalEntryHeader> _journalEntryRepo;
        private readonly IRepository<JournalEntryLine> _journalEntryLineRepo;
        private readonly IRepository<GeneralLedgerLine> _generalLedgerLineRepository;
        private readonly IRepository<FinancialYear> _fiscalYearRepo;
        private readonly IRepository<TaxGroup> _taxGroupRepo;
        private readonly IRepository<ItemTaxGroup> _itemTaxGroupRepo;
        private readonly IRepository<PaymentTerm> _paymentTermRepo;
        private readonly IRepository<Bank> _bankRepo;
        private readonly IRepository<Item> _itemRepo;
        private readonly IRepository<GeneralLedgerSetting> _glSettingRepo;
        private readonly IRepository<MainContraAccount> _maincontraAccount;
        private readonly IRepository<Customer> _customerRepo;
        private readonly IRepository<Vendor> _vendorRepo;
        private readonly IRepository<GeneralLedgerSetting> _generalLedgerSettingRepo;
        private readonly IRepository<SalesInvoiceHeader> _salesInvoiceRepo;
        private readonly IRepository<SalesReceiptHeader> _salesReceiptRepo;
        private readonly IRepository<CustomerAllocation> _customerAllocationRepo;
        private readonly IRepository<PurchaseInvoiceHeader> _purchaseInvoiceRepo;
        private readonly IRepository<VendorPayment> _vendorPaymentRepo;
        private readonly IRepository<CompanySetting> _companySettingRepo;

        public FinancialService(IInventoryService inventoryService,
            ITaxService taxService,
            IRepository<GeneralLedgerHeader> generalLedgerRepository,
            IRepository<GeneralLedgerLine> generalLedgerLineRepository,
            IRepository<Account> accountRepo,
            IRepository<AccountClass> accountClassRepo,
            IRepository<Tax> taxRepo,
            IRepository<JournalEntryHeader> journalEntryRepo,
            IRepository<JournalEntryLine> journalEntryLineRepo,
            IRepository<FinancialYear> fiscalYearRepo,
            IRepository<TaxGroup> taxGroupRepo,
            IRepository<ItemTaxGroup> itemTaxGroupRepo,
            IRepository<PaymentTerm> paymentTermRepo,
            IRepository<Bank> bankRepo,
            IRepository<Item> itemRepo,
            IRepository<GeneralLedgerSetting> glSettingRepo,
            IRepository<CompanySetting> companySettingRepo,
            IRepository<MainContraAccount> maincontraAccount = null,
            IRepository<Customer> customerRepo = null,
            IRepository<Vendor> vendorRepo = null,
            IRepository<GeneralLedgerSetting> generalLedgerSettingRepo = null,
            IRepository<SalesInvoiceHeader> salesInvoiceRepo = null,
            IRepository<SalesReceiptHeader> salesReceiptRepo = null,
            IRepository<CustomerAllocation> customerAllocationRepo = null,
            IRepository<PurchaseInvoiceHeader> purchaseInvoiceRepo = null,
            IRepository<VendorPayment> vendorPaymentRepo = null
            )
            : base(null, null, paymentTermRepo, bankRepo)
        {
            _inventoryService = inventoryService;
            _taxService = taxService;
            _companySettingRepo = companySettingRepo ?? throw new ArgumentNullException(nameof(companySettingRepo));
            _generalLedgerRepository = generalLedgerRepository;
            _accountRepo = accountRepo;
            _accountClassRepo = accountClassRepo;
            _taxRepo = taxRepo;
            _journalEntryRepo = journalEntryRepo;
            _journalEntryLineRepo = journalEntryLineRepo;
            _generalLedgerLineRepository = generalLedgerLineRepository;
            _fiscalYearRepo = fiscalYearRepo;
            _taxGroupRepo = taxGroupRepo;
            _itemTaxGroupRepo = itemTaxGroupRepo;
            _paymentTermRepo = paymentTermRepo;
            _bankRepo = bankRepo;
            _itemRepo = itemRepo;
            _glSettingRepo = glSettingRepo;
            _maincontraAccount = maincontraAccount;
            _customerRepo = customerRepo;
            _vendorRepo = vendorRepo;
            _generalLedgerSettingRepo = generalLedgerSettingRepo;
            _salesInvoiceRepo = salesInvoiceRepo;
            _salesReceiptRepo = salesReceiptRepo;
            _customerAllocationRepo = customerAllocationRepo;
            _purchaseInvoiceRepo = purchaseInvoiceRepo;
            _vendorPaymentRepo = vendorPaymentRepo;
        }

        public const int GeneralLedgerDefaultPageSize = 100;
        public const int GeneralLedgerMaxPageSize = 500;

        /// <inheritdoc />
        public Dto.Financial.GeneralLedgerTransactionPage GetGeneralLedgerTransactions(
            DateTime? from = null, DateTime? to = null, string accountCode = null,
            int? transactionNo = null, DocumentTypes? documentType = null, int skip = 0, int take = GeneralLedgerDefaultPageSize)
        {
            if (skip < 0) throw new ArgumentOutOfRangeException(nameof(skip), "Skip must be zero or greater.");
            if (take < 1 || take > GeneralLedgerMaxPageSize)
                throw new ArgumentOutOfRangeException(nameof(take), $"Take must be between 1 and {GeneralLedgerMaxPageSize}.");

            // Date-only / midnight "to" values mean the whole calendar day (same idea as the SPA's T23:59:59.9999999).
            var fromBound = from.HasValue ? DateTime.SpecifyKind(from.Value, DateTimeKind.Unspecified) : (DateTime?)null;
            var toBound = to.HasValue ? InclusiveLedgerEnd(DateTime.SpecifyKind(to.Value, DateTimeKind.Unspecified)) : (DateTime?)null;
            if (fromBound.HasValue && toBound.HasValue && fromBound.Value > toBound.Value)
                throw new ArgumentException("The start date must not be after the end date.");

            var query = _generalLedgerRepository.Table;
            if (fromBound.HasValue) query = query.Where(h => h.Date >= fromBound.Value);
            if (toBound.HasValue) query = query.Where(h => h.Date <= toBound.Value);
            if (!string.IsNullOrWhiteSpace(accountCode))
                query = query.Where(h => h.GeneralLedgerLines.Any(l => l.Account.AccountCode == accountCode));
            if (transactionNo.HasValue) query = query.Where(h => h.Id == transactionNo.Value);
            if (documentType.HasValue) query = query.Where(h => h.DocumentType == documentType.Value);

            var totalCount = query.Count();
            var transactions = query.OrderByDescending(h => h.Date).ThenByDescending(h => h.Id)
                .Skip(skip).Take(take)
                .Select(h => new Dto.Financial.GeneralLedgerTransaction
                {
                    Id = h.Id, Date = h.Date, DocumentType = h.DocumentType.ToString(),
                    Description = h.Description, SourceDocumentId = h.SourceDocumentId,
                    ReversedGeneralLedgerHeaderId = h.ReversedGeneralLedgerHeaderId,
                    Lines = h.GeneralLedgerLines.OrderBy(l => l.Id).Select(l => new Dto.Financial.GeneralLedgerTransactionLine
                    {
                        Id = l.Id, AccountId = l.AccountId,
                        AccountCode = l.Account.AccountCode, AccountName = l.Account.AccountName,
                        Debit = l.DrCr == DrOrCrSide.Dr ? l.Amount : 0,
                        Credit = l.DrCr == DrOrCrSide.Cr ? l.Amount : 0
                    }).ToList()
                }).ToList();

            foreach (var transaction in transactions)
            {
                transaction.DocumentTypeLabel = System.Text.RegularExpressions.Regex.Replace(transaction.DocumentType, "([a-z])([A-Z])", "$1 $2");
                transaction.TotalDebit = transaction.Lines.Sum(l => l.Debit);
                transaction.TotalCredit = transaction.Lines.Sum(l => l.Credit);
                if (!transaction.SourceDocumentId.HasValue) continue;
                var id = transaction.SourceDocumentId.Value;
                switch (transaction.DocumentType)
                {
                    case nameof(DocumentTypes.SalesInvoice): transaction.SourcePath = "/sales/" + id; break;
                    case nameof(DocumentTypes.PurchaseInvoice):
                    // PayBill records the bill ID, not the vendor payment ID, as its source.
                    case nameof(DocumentTypes.PurchaseInvoicePayment):
                        transaction.SourcePath = "/purchases/" + id; break;
                    case nameof(DocumentTypes.JournalEntry): transaction.SourcePath = "/financials/journals?id=" + id; break;
                    case nameof(DocumentTypes.InventoryAdjustment): transaction.SourcePath = "/items/inventory"; break;
                }
            }

            return new Dto.Financial.GeneralLedgerTransactionPage
            {
                Items = transactions,
                TotalCount = totalCount,
                Skip = skip,
                Take = take
            };
        }

        /// <summary>Keeps an explicit end-of-day timestamp; expands midnight/date-only bounds to the last tick of that day.</summary>
        private static DateTime InclusiveLedgerEnd(DateTime to) =>
            to.TimeOfDay == TimeSpan.Zero ? to.Date.AddDays(1).AddTicks(-1) : to;

        public FinancialYear CurrentFiscalYear()
        {
            var query = (from fy in _fiscalYearRepo.Table
                         where fy.IsActive == true
                         select fy).FirstOrDefault();

            return query;
        }

        /// <summary>Rejects postings dated before the company's books start date (resolved in the current tenant scope).</summary>
        private void EnsureOnOrAfterBooksStart(DateTime date)
        {
            var booksStart = _companySettingRepo.Table.Select(s => (DateTime?)s.BooksStartDate).FirstOrDefault();
            if (booksStart.HasValue && date.Date < booksStart.Value.Date)
                throw new BooksStartViolationException(date, booksStart.Value);
        }

        public GeneralLedgerHeader CreateGeneralLedgerHeader(DocumentTypes documentType, DateTime date, string description, int? sourceDocumentId = null, int? reversedGeneralLedgerHeaderId = null)
        {
            EnsureOnOrAfterBooksStart(date);
            var entry = new GeneralLedgerHeader()
            {
                DocumentType = documentType,
                Date = date,
                Description = description,
                SourceDocumentId = sourceDocumentId > 0 ? sourceDocumentId : null,
                ReversedGeneralLedgerHeaderId = reversedGeneralLedgerHeaderId > 0 ? reversedGeneralLedgerHeaderId : null,
            };
            return entry;
        }

        public GeneralLedgerLine CreateGeneralLedgerLine(DrOrCrSide DrCr, int accountId, decimal amount)
        {
            var line = new GeneralLedgerLine()
            {
                DrCr = DrCr,
                AccountId = accountId,
                Amount = amount,
            };
            return line;
        }

        public IEnumerable<Account> GetAccounts()
        {
            var accounts = _accountRepo.GetAllIncluding(c => c.Company,
                a => a.AccountClass,
                c => c.ChildAccounts,
                g => g.GeneralLedgerLines,
                p => p.ParentAccount)
                .AsEnumerable();

            return accounts;
        }

        public Account GetAccount(int id)
        {
            var account = _accountRepo.GetAllIncluding(a => a.GeneralLedgerLines,
                a => a.AccountClass,
                a => a.ChildAccounts,
                a => a.ParentAccount,
                a => a.Company)
                .Where(a => a.Id == id)
                .FirstOrDefault();

            return account;
        }

        public Account GetAccountByAccountCode(string accountcode)
        {
            var account = _accountRepo.GetAllIncluding(a => a.GeneralLedgerLines,
                a => a.AccountClass,
                a => a.ChildAccounts,
                a => a.ParentAccount,
                a => a.Company)
                .Where(a => a.AccountCode == accountcode)
                //.OrderByDescending(a => a.Id)
                .FirstOrDefault();

            return account;
        }

        public IEnumerable<Tax> GetTaxes()
        {
            var query = from f in _taxRepo.Table
                        select f;
            return query.AsEnumerable();
        }

        public IEnumerable<ItemTaxGroup> GetItemTaxGroups()
        {
            var query = from f in _itemTaxGroupRepo.Table
                        select f;
            return query;
        }

        public IEnumerable<TaxGroup> GetTaxGroups()
        {
            var query = from f in _taxGroupRepo.Table
                        select f;
            return query;
        }

        public void SaveGeneralLedgerEntry(GeneralLedgerHeader entry)
        {
            if (ValidateGeneralLedgerEntry(entry))
            {
                _generalLedgerRepository.Insert(entry);
            }
        }

        public void UpdateGeneralLedgerHeader(GeneralLedgerHeader entry)
        {
            _generalLedgerRepository.Update(entry);
        }

        public bool ValidateGeneralLedgerEntry(GeneralLedgerHeader glEntry)
        {
            if (!FinancialHelper.DrCrEqualityValidated(glEntry))
                throw new InvalidOperationException("Debit/Credit are not equal.");

            if (!FinancialHelper.NoLineAmountIsEqualToZero(glEntry))
                throw new InvalidOperationException("One or more line(s) amount is zero.");

            var activeFiscalYear = _fiscalYearRepo.Table
                .FirstOrDefault(fy => fy.IsActive && glEntry.Date >= fy.StartDate && glEntry.Date <= fy.EndDate);

            if (activeFiscalYear == null)
                throw new InvalidOperationException($"Posting date {glEntry.Date:yyyy-MM-dd} is out of range. No active financial year covers this period.");

            foreach (var line in glEntry.GeneralLedgerLines)
            {
                var account = _accountRepo.GetAllIncluding(a => a.ChildAccounts)
                    .Where(a => a.Id == line.AccountId)
                    .FirstOrDefault();

                if (account == null)
                    throw new InvalidOperationException($"Account ID {line.AccountId} does not exist.");

                if (!account.CanPost())
                    throw new InvalidOperationException($"Account '{account.AccountName}' ({account.AccountCode}) is a summary/parent account and cannot be posted to directly.");
            }

            return true;
        }

        public IEnumerable<JournalEntryLine> GetJournalEntryLines()
        {
            var query = _journalEntryLineRepo.GetAllIncluding(j => j.JournalEntryHeader, a => a.Account, h => h.JournalEntryHeader.GeneralLedgerHeader);
            return query.AsEnumerable();
        }

        public IEnumerable<JournalEntryHeader> GetJournalEntries()
        {
            var journalEntries = _journalEntryRepo.GetAllIncluding(je => je.JournalEntryLines,
                je => je.Party,
                je => je.GeneralLedgerHeader);

            return journalEntries;
        }

        public void AddJournalEntry(JournalEntryHeader journalEntry)
        {
            journalEntry.Posted = false;

            _journalEntryRepo.Insert(journalEntry);
        }

        public ICollection<TrialBalance> TrialBalance(DateTime? from = default(DateTime?), DateTime? to = default(DateTime?), AccountingBasis basis = AccountingBasis.Accrual)
        {
            if (from.HasValue && to.HasValue && from.Value > to.Value)
                throw new ArgumentException("The start date must not be after the end date.");

            if (basis == AccountingBasis.Cash)
                return CashBasisTrialBalance(from, to);

            var lines = _generalLedgerLineRepository.Table;
            if (from.HasValue)
                lines = lines.Where(l => l.GeneralLedgerHeader.Date >= from.Value);
            if (to.HasValue)
                lines = lines.Where(l => l.GeneralLedgerHeader.Date <= to.Value);

            return lines
                .GroupBy(l => new { l.AccountId, l.Account.AccountCode, l.Account.AccountName })
                .Select(g => new TrialBalance
                {
                    AccountId = g.Key.AccountId,
                    AccountCode = g.Key.AccountCode,
                    AccountName = g.Key.AccountName,
                    Debit = g.Sum(l => l.DrCr == DrOrCrSide.Dr ? l.Amount : 0m),
                    Credit = g.Sum(l => l.DrCr == DrOrCrSide.Cr ? l.Amount : 0m)
                })
                .OrderBy(a => a.AccountCode)
                .ToList();
        }

        private ICollection<TrialBalance> CashBasisTrialBalance(DateTime? from, DateTime? to)
        {
            var accounts = _accountRepo.Table
                .Select(a => new { a.Id, a.AccountClassId, a.AccountCode, a.AccountName })
                .ToDictionary(a => a.Id);

            var purchasingTaxAccountIds = _taxRepo?.Table
                .Where(t => t.PurchasingAccountId.HasValue)
                .Select(t => t.PurchasingAccountId.Value)
                .ToHashSet()
                ?? new HashSet<int>();

            bool IsCapitalizedAssetAccount(int accountId) =>
                accounts.TryGetValue(accountId, out var acc)
                && acc.AccountClassId == (int)AccountClasses.Assets
                && acc.AccountCode != "10160"
                && !purchasingTaxAccountIds.Contains(accountId);

            var allGlLines = _generalLedgerLineRepository.Table
                .Select(l => new
                {
                    l.Id,
                    l.GeneralLedgerHeaderId,
                    HeaderDate = l.GeneralLedgerHeader.Date,
                    DocumentType = l.GeneralLedgerHeader.DocumentType,
                    l.AccountId,
                    l.DrCr,
                    l.Amount
                })
                .ToList();

            var linesByHeader = allGlLines
                .GroupBy(l => l.GeneralLedgerHeaderId)
                .ToDictionary(g => g.Key, g => g.OrderBy(l => l.Id).ToList());

            var suppressedLineAmounts = new Dictionary<int, decimal>();
            var totals = new Dictionary<int, (decimal Debit, decimal Credit)>();

            // Accrual customer-allocation journals reclassify Customer Advances to A/R; cash basis recognizes revenue directly against the receipt credit.
            foreach (var allocLine in allGlLines.Where(l => l.DocumentType == DocumentTypes.CustomerAllocation))
                suppressedLineAmounts[allocLine.Id] = allocLine.Amount;

            void AddPosting(int accountId, DrOrCrSide drCr, decimal amount, DateTime date)
            {
                if (amount == 0m)
                    return;
                if (from.HasValue && date < from.Value)
                    return;
                if (to.HasValue && date > to.Value)
                    return;
                if (amount < 0m)
                {
                    drCr = drCr == DrOrCrSide.Dr ? DrOrCrSide.Cr : DrOrCrSide.Dr;
                    amount = -amount;
                }
                var current = totals.GetValueOrDefault(accountId);
                if (drCr == DrOrCrSide.Dr)
                    totals[accountId] = (current.Debit + amount, current.Credit);
                else if (drCr == DrOrCrSide.Cr)
                    totals[accountId] = (current.Debit, current.Credit + amount);
            }

            if (_salesInvoiceRepo != null)
            {
                var customerArAccounts = _customerRepo?.Table
                    .Select(c => new { c.Id, c.AccountsReceivableAccountId })
                    .ToDictionary(c => c.Id, c => c.AccountsReceivableAccountId)
                    ?? new Dictionary<int, int?>();

                var receiptGlMap = _salesReceiptRepo?.Table
                    .Where(r => r.GeneralLedgerHeaderId.HasValue)
                    .Select(r => new { r.Id, GlHeaderId = r.GeneralLedgerHeaderId.Value })
                    .ToDictionary(r => r.Id, r => r.GlHeaderId)
                    ?? new Dictionary<int, int>();

                var allocationsByInvoice = (_customerAllocationRepo?.Table
                        .Select(a => new { a.Id, a.SalesInvoiceHeaderId, a.SalesReceiptHeaderId, a.GeneralLedgerHeaderId, a.Date, a.Amount })
                        .ToList() ?? new())
                    .GroupBy(a => a.SalesInvoiceHeaderId)
                    .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Date).ThenBy(a => a.Id).ToList());

                var invoices = _salesInvoiceRepo.Table
                    .Where(i => i.GeneralLedgerHeaderId.HasValue)
                    .Select(i => new
                    {
                        i.Id,
                        i.CustomerId,
                        i.Status,
                        GlHeaderId = i.GeneralLedgerHeaderId.Value,
                        i.VoidGeneralLedgerHeaderId
                    })
                    .ToList();

                foreach (var invoice in invoices)
                {
                    if (!linesByHeader.TryGetValue(invoice.GlHeaderId, out var invLines) || invLines.Count == 0)
                        continue;

                    // Stock-issue pairs (Dr COGS, Cr Inventory) stay on the invoice date; only A/R and its revenue/tax/discount/shipping offsets wait for cash.
                    var stockLineIds = new HashSet<int>();
                    foreach (var invCredit in invLines.Where(l => l.DrCr == DrOrCrSide.Cr && accounts.TryGetValue(l.AccountId, out var acc) && acc.AccountClassId == (int)AccountClasses.Assets))
                    {
                        var matchingCogs = invLines.FirstOrDefault(l =>
                            !stockLineIds.Contains(l.Id)
                            && l.DrCr == DrOrCrSide.Dr
                            && l.Amount == invCredit.Amount
                            && accounts.TryGetValue(l.AccountId, out var cogsAcc)
                            && cogsAcc.AccountClassId == (int)AccountClasses.Expense);
                        if (matchingCogs != null)
                        {
                            stockLineIds.Add(invCredit.Id);
                            stockLineIds.Add(matchingCogs.Id);
                        }
                    }

                    var nonStockLines = invLines.Where(l => !stockLineIds.Contains(l.Id)).ToList();
                    var expectedArAccountId = customerArAccounts.GetValueOrDefault(invoice.CustomerId);
                    var arLine = nonStockLines.FirstOrDefault(l => l.DrCr == DrOrCrSide.Dr && expectedArAccountId.HasValue && l.AccountId == expectedArAccountId.Value)
                        ?? nonStockLines.FirstOrDefault(l => l.DrCr == DrOrCrSide.Dr && accounts.TryGetValue(l.AccountId, out var acc) && acc.AccountClassId == (int)AccountClasses.Assets)
                        ?? nonStockLines.FirstOrDefault(l => l.DrCr == DrOrCrSide.Dr);

                    if (arLine == null || arLine.Amount <= 0m)
                        continue;

                    var offsetLines = nonStockLines.Where(l => l.Id != arLine.Id).ToList();
                    foreach (var line in nonStockLines)
                        suppressedLineAmounts[line.Id] = line.Amount;

                    if (invoice.VoidGeneralLedgerHeaderId.HasValue && linesByHeader.TryGetValue(invoice.VoidGeneralLedgerHeaderId.Value, out var voidLines))
                    {
                        var voidStockIds = new HashSet<int>();
                        foreach (var invDebit in voidLines.Where(l => l.DrCr == DrOrCrSide.Dr && accounts.TryGetValue(l.AccountId, out var acc) && acc.AccountClassId == (int)AccountClasses.Assets))
                        {
                            var matchingCogs = voidLines.FirstOrDefault(l =>
                                !voidStockIds.Contains(l.Id)
                                && l.DrCr == DrOrCrSide.Cr
                                && l.Amount == invDebit.Amount
                                && accounts.TryGetValue(l.AccountId, out var cogsAcc)
                                && cogsAcc.AccountClassId == (int)AccountClasses.Expense);
                            if (matchingCogs != null)
                            {
                                voidStockIds.Add(invDebit.Id);
                                voidStockIds.Add(matchingCogs.Id);
                            }
                        }
                        foreach (var line in voidLines.Where(l => !voidStockIds.Contains(l.Id)))
                            suppressedLineAmounts[line.Id] = line.Amount;
                        continue;
                    }

                    if (invoice.Status == SalesInvoiceStatus.Void || offsetLines.Count == 0)
                        continue;

                    if (!allocationsByInvoice.TryGetValue(invoice.Id, out var allocations) || allocations.Count == 0)
                        continue;

                    var invoiceTotal = arLine.Amount;
                    var signedOffsets = offsetLines
                        .Select(l => (l.AccountId, SignedAmount: l.DrCr == DrOrCrSide.Cr ? l.Amount : -l.Amount))
                        .ToList();
                    var prevCumulativeByLine = new decimal[signedOffsets.Count];
                    var cumulativePaid = 0m;

                    foreach (var allocation in allocations)
                    {
                        if (allocation.GeneralLedgerHeaderId.HasValue
                            && linesByHeader.TryGetValue(allocation.GeneralLedgerHeaderId.Value, out var allocGlLines))
                        {
                            foreach (var allocGlLine in allocGlLines)
                                suppressedLineAmounts[allocGlLine.Id] = allocGlLine.Amount;
                        }

                        if (allocation.Amount <= 0m || cumulativePaid >= invoiceTotal)
                            continue;

                        var desiredSettle = Math.Min(allocation.Amount, invoiceTotal - cumulativePaid);
                        if (desiredSettle <= 0m)
                            continue;

                        var settledAmount = desiredSettle;
                        var recognitionDate = allocation.Date;
                        if (receiptGlMap.TryGetValue(allocation.SalesReceiptHeaderId, out var receiptGlId)
                            && linesByHeader.TryGetValue(receiptGlId, out var receiptLines))
                        {
                            var receiptCreditLines = receiptLines
                                .Where(l => l.DrCr == DrOrCrSide.Cr)
                                .OrderByDescending(l => l.AccountId == arLine.AccountId)
                                .ThenBy(l => l.Id)
                                .ToList();
                            var availableReceiptCredit = receiptCreditLines.Sum(l =>
                                Math.Max(0m, l.Amount - suppressedLineAmounts.GetValueOrDefault(l.Id)));
                            settledAmount = Math.Min(desiredSettle, availableReceiptCredit);
                            if (settledAmount <= 0m)
                                continue;

                            var toSuppress = settledAmount;
                            foreach (var creditLine in receiptCreditLines)
                            {
                                if (toSuppress <= 0m)
                                    break;
                                var lineAvailable = Math.Max(0m, creditLine.Amount - suppressedLineAmounts.GetValueOrDefault(creditLine.Id));
                                if (lineAvailable <= 0m)
                                    continue;
                                var take = Math.Min(toSuppress, lineAvailable);
                                suppressedLineAmounts[creditLine.Id] = suppressedLineAmounts.GetValueOrDefault(creditLine.Id) + take;
                                toSuppress -= take;
                                recognitionDate = creditLine.HeaderDate;
                            }
                        }

                        var nextCumulativePaid = cumulativePaid + settledAmount;
                        var allocatedSumBeforeLast = 0m;
                        for (var i = 0; i < signedOffsets.Count; i++)
                        {
                            decimal delta;
                            if (i < signedOffsets.Count - 1)
                            {
                                var targetCumulative = Math.Round(signedOffsets[i].SignedAmount * nextCumulativePaid / invoiceTotal, 2, MidpointRounding.AwayFromZero);
                                delta = targetCumulative - prevCumulativeByLine[i];
                                prevCumulativeByLine[i] = targetCumulative;
                                allocatedSumBeforeLast += delta;
                            }
                            else
                            {
                                delta = settledAmount - allocatedSumBeforeLast;
                                prevCumulativeByLine[i] += delta;
                            }

                            if (delta > 0m)
                                AddPosting(signedOffsets[i].AccountId, DrOrCrSide.Cr, delta, recognitionDate);
                            else if (delta < 0m)
                                AddPosting(signedOffsets[i].AccountId, DrOrCrSide.Dr, -delta, recognitionDate);
                        }

                        cumulativePaid = nextCumulativePaid;
                    }
                }
            }

            if (_purchaseInvoiceRepo != null)
            {
                var vendorApAccounts = _vendorRepo?.Table
                    .Select(v => new { v.Id, v.AccountsPayableAccountId })
                    .ToDictionary(v => v.Id, v => v.AccountsPayableAccountId)
                    ?? new Dictionary<int, int?>();

                var paymentsByBill = (_vendorPaymentRepo?.Table
                        .Where(p => p.PurchaseInvoiceHeaderId.HasValue)
                        .Select(p => new
                        {
                            p.Id,
                            BillId = p.PurchaseInvoiceHeaderId.Value,
                            p.GeneralLedgerHeaderId,
                            p.Date,
                            p.Amount
                        })
                        .ToList() ?? new())
                    .GroupBy(p => p.BillId)
                    .ToDictionary(g => g.Key, g => g.OrderBy(p => p.Date).ThenBy(p => p.Id).ToList());

                var bills = _purchaseInvoiceRepo.Table
                    .Where(b => b.GeneralLedgerHeaderId.HasValue)
                    .Select(b => new
                    {
                        b.Id,
                        b.VendorId,
                        b.Status,
                        GlHeaderId = b.GeneralLedgerHeaderId.Value,
                        b.VoidGeneralLedgerHeaderId
                    })
                    .ToList();

                foreach (var bill in bills)
                {
                    if (!linesByHeader.TryGetValue(bill.GlHeaderId, out var billLines) || billLines.Count == 0)
                        continue;

                    var expectedApAccountId = bill.VendorId.HasValue ? vendorApAccounts.GetValueOrDefault(bill.VendorId.Value) : null;
                    var apLine = billLines.LastOrDefault(l => l.DrCr == DrOrCrSide.Cr && expectedApAccountId.HasValue && l.AccountId == expectedApAccountId.Value)
                        ?? billLines.Where(l => l.DrCr == DrOrCrSide.Cr && accounts.TryGetValue(l.AccountId, out var acc) && acc.AccountClassId == (int)AccountClasses.Liabilities)
                            .OrderByDescending(l => l.Amount).FirstOrDefault()
                        ?? billLines.Where(l => l.DrCr == DrOrCrSide.Cr).OrderByDescending(l => l.Amount).FirstOrDefault();

                    if (apLine == null || apLine.Amount <= 0m)
                        continue;

                    var offsetLines = billLines.Where(l => l.Id != apLine.Id).ToList();
                    // Keep balance-sheet asset capitalization (e.g., inventory, fixed assets) on the bill date; defer only P&L and tax lines until payment.
                    var deferredLines = offsetLines.Where(l => !IsCapitalizedAssetAccount(l.AccountId)).ToList();
                    var deferredNet = deferredLines.Sum(l => l.DrCr == DrOrCrSide.Dr ? l.Amount : -l.Amount);
                    foreach (var line in deferredLines)
                        suppressedLineAmounts[line.Id] = line.Amount;
                    suppressedLineAmounts[apLine.Id] = Math.Clamp(deferredNet, 0m, apLine.Amount);

                    if (bill.VoidGeneralLedgerHeaderId.HasValue && linesByHeader.TryGetValue(bill.VoidGeneralLedgerHeaderId.Value, out var voidLines))
                    {
                        var voidApLine = voidLines.LastOrDefault(l => l.DrCr == DrOrCrSide.Dr && l.AccountId == apLine.AccountId)
                            ?? voidLines.Where(l => l.DrCr == DrOrCrSide.Dr).OrderByDescending(l => l.Amount).FirstOrDefault();
                        foreach (var line in voidLines.Where(l => (voidApLine == null || l.Id != voidApLine.Id) && !IsCapitalizedAssetAccount(l.AccountId)))
                            suppressedLineAmounts[line.Id] = line.Amount;
                        if (voidApLine != null)
                            suppressedLineAmounts[voidApLine.Id] = Math.Clamp(deferredNet, 0m, voidApLine.Amount);
                        continue;
                    }

                    if (bill.Status == PurchaseInvoiceStatus.Void || deferredLines.Count == 0)
                        continue;

                    if (!paymentsByBill.TryGetValue(bill.Id, out var payments) || payments.Count == 0)
                        continue;

                    var billTotal = apLine.Amount;
                    var signedOffsets = offsetLines
                        .Select(l => (
                            l.AccountId,
                            SignedAmount: l.DrCr == DrOrCrSide.Dr ? l.Amount : -l.Amount,
                            IsCapitalized: IsCapitalizedAssetAccount(l.AccountId)))
                        .ToList();
                    var prevCumulativeByLine = new decimal[signedOffsets.Count];
                    var cumulativePaid = 0m;

                    foreach (var payment in payments)
                    {
                        if (payment.Amount <= 0m || cumulativePaid >= billTotal)
                            continue;

                        var desiredSettle = Math.Min(payment.Amount, billTotal - cumulativePaid);
                        if (desiredSettle <= 0m)
                            continue;

                        var settledAmount = desiredSettle;
                        var recognitionDate = payment.Date;
                        int? paymentDrLineId = null;
                        decimal availablePaymentDebit = 0m;
                        if (payment.GeneralLedgerHeaderId.HasValue
                            && linesByHeader.TryGetValue(payment.GeneralLedgerHeaderId.Value, out var paymentLines))
                        {
                            var paymentDrLine = paymentLines.FirstOrDefault(l => l.DrCr == DrOrCrSide.Dr && l.AccountId == apLine.AccountId)
                                ?? paymentLines.FirstOrDefault(l => l.DrCr == DrOrCrSide.Dr);
                            if (paymentDrLine != null)
                            {
                                availablePaymentDebit = Math.Max(0m, paymentDrLine.Amount - suppressedLineAmounts.GetValueOrDefault(paymentDrLine.Id));
                                settledAmount = Math.Min(desiredSettle, availablePaymentDebit);
                                if (settledAmount <= 0m)
                                    continue;
                                paymentDrLineId = paymentDrLine.Id;
                                recognitionDate = paymentDrLine.HeaderDate;
                            }
                        }

                        var nextCumulativePaid = cumulativePaid + settledAmount;
                        var allocatedSumBeforeLast = 0m;
                        var deferredDeltaSum = 0m;
                        for (var i = 0; i < signedOffsets.Count; i++)
                        {
                            decimal delta;
                            if (i < signedOffsets.Count - 1)
                            {
                                var targetCumulative = Math.Round(signedOffsets[i].SignedAmount * nextCumulativePaid / billTotal, 2, MidpointRounding.AwayFromZero);
                                delta = targetCumulative - prevCumulativeByLine[i];
                                prevCumulativeByLine[i] = targetCumulative;
                                allocatedSumBeforeLast += delta;
                            }
                            else
                            {
                                delta = settledAmount - allocatedSumBeforeLast;
                                prevCumulativeByLine[i] += delta;
                            }

                            if (!signedOffsets[i].IsCapitalized)
                            {
                                deferredDeltaSum += delta;
                                if (delta > 0m)
                                    AddPosting(signedOffsets[i].AccountId, DrOrCrSide.Dr, delta, recognitionDate);
                                else if (delta < 0m)
                                    AddPosting(signedOffsets[i].AccountId, DrOrCrSide.Cr, -delta, recognitionDate);
                            }
                        }

                        if (paymentDrLineId.HasValue)
                        {
                            var suppressDebit = Math.Clamp(deferredDeltaSum, 0m, availablePaymentDebit);
                            suppressedLineAmounts[paymentDrLineId.Value] = suppressedLineAmounts.GetValueOrDefault(paymentDrLineId.Value) + suppressDebit;
                        }

                        cumulativePaid = nextCumulativePaid;
                    }
                }
            }

            foreach (var line in allGlLines)
            {
                var remaining = line.Amount - suppressedLineAmounts.GetValueOrDefault(line.Id);
                if (remaining > 0m)
                    AddPosting(line.AccountId, line.DrCr, remaining, line.HeaderDate);
            }

            return totals
                .Where(kvp => (kvp.Value.Debit != 0m || kvp.Value.Credit != 0m) && accounts.ContainsKey(kvp.Key))
                .Select(kvp =>
                {
                    var account = accounts[kvp.Key];
                    return new TrialBalance
                    {
                        AccountId = account.Id,
                        AccountCode = account.AccountCode,
                        AccountName = account.AccountName,
                        Debit = kvp.Value.Debit,
                        Credit = kvp.Value.Credit
                    };
                })
                .OrderBy(a => a.AccountCode)
                .ToList();
        }

        public ICollection<BalanceSheet> BalanceSheet(DateTime? from = default(DateTime?), DateTime? to = default(DateTime?), AccountingBasis basis = AccountingBasis.Accrual)
        {
            if (from.HasValue && to.HasValue && from.Value > to.Value)
                throw new ArgumentException("The start date must not be after the end date.");

            // A position statement includes opening balances, not just movement since 'from'.
            var balances = TrialBalance(null, to, basis).ToDictionary(a => a.AccountId);
            var accounts = _accountRepo.Table.Where(a => a.AccountClassId >= 1 && a.AccountClassId <= 5)
                .Select(a => new { a.Id, a.AccountClassId, a.AccountCode, a.AccountName }).ToList();
            var balanceSheet = accounts.Where(a => a.AccountClassId <= 3)
                .Select(a =>
                {
                    TrialBalance balance;
                    var net = balances.TryGetValue(a.Id, out balance) ? balance.Debit - balance.Credit : 0m;
                    return new BalanceSheet
                    {
                        AccountId = a.Id,
                        AccountClassId = a.AccountClassId,
                        AccountCode = a.AccountCode,
                        AccountName = a.AccountName,
                        Amount = a.AccountClassId == 1 ? net : -net
                    };
                }).ToList();

            // Closed income accounts already net to zero; only unclosed earnings are added.
            var netIncome = accounts.Where(a => a.AccountClassId == 4 || a.AccountClassId == 5)
                .Sum(a => balances.ContainsKey(a.Id) ? balances[a.Id].Credit - balances[a.Id].Debit : 0m);
            if (netIncome != 0m)
                balanceSheet.Add(new BalanceSheet
                {
                    AccountId = 0,
                    AccountClassId = 3,
                    AccountCode = "",
                    AccountName = "Retained Surplus / Accumulated Profit (unclosed net income)",
                    Amount = netIncome
                });
            return balanceSheet;
        }

        public ICollection<IncomeStatement> IncomeStatement(DateTime? from = default(DateTime?), DateTime? to = default(DateTime?), AccountingBasis basis = AccountingBasis.Accrual)
        {
            var balances = TrialBalance(from, to, basis).ToDictionary(a => a.AccountId);
            return _accountRepo.Table.Where(a => a.AccountClassId == 4 || a.AccountClassId == 5)
                .Select(a => new { a.Id, a.AccountClassId, a.AccountCode, a.AccountName })
                .ToList()
                .Select(a =>
                {
                    TrialBalance balance;
                    var net = balances.TryGetValue(a.Id, out balance) ? balance.Debit - balance.Credit : 0m;
                    return new IncomeStatement
                    {
                        AccountId = a.Id,
                        AccountCode = a.AccountCode,
                        AccountName = a.AccountName,
                        IsExpense = a.AccountClassId == 5,
                        Amount = a.AccountClassId == 5 ? net : -net
                    };
                }).OrderBy(a => a.AccountCode).ToList();
        }

        public ICollection<MasterGeneralLedger> MasterGeneralLedger(DateTime? from = default(DateTime?),
            DateTime? to = default(DateTime?),
            string accountCode = null,
            int? transactionNo = null)
        {
            var allDr = (from dr in _generalLedgerLineRepository.GetAllIncluding(l => l.GeneralLedgerHeader, a => a.Account).AsEnumerable()
                         where dr.DrCr == DrOrCrSide.Dr
                         //&& GeneralLedgerHelper.IsBetween(dr.GLHeader.Date, (DateTime)fromDate, (DateTime)toDate) == true
                         select new MasterGeneralLedger
                         {
                             Id = dr.Id,
                             TransactionNo = dr.GeneralLedgerHeader.Id,
                             AccountId = dr.AccountId,
                             AccountCode = dr.Account.AccountCode,
                             AccountName = dr.Account.AccountName,
                             Date = dr.GeneralLedgerHeader.Date,
                             Debit = dr.Amount,
                             Credit = (decimal)0,
                             //CurrencyId = dr.CurrencyId,
                             DocumentType = Enum.GetName(typeof(DocumentTypes), dr.GeneralLedgerHeader.DocumentType)
                         });

            var allCr = (from cr in _generalLedgerLineRepository.GetAllIncluding(l => l.GeneralLedgerHeader, a => a.Account).AsEnumerable()
                         where cr.DrCr == DrOrCrSide.Cr
                         //&& GeneralLedgerHelper.IsBetween(cr.GLHeader.Date, (DateTime)fromDate, (DateTime)toDate) == true
                         select new MasterGeneralLedger
                         {
                             Id = cr.Id,
                             TransactionNo = cr.GeneralLedgerHeader.Id,
                             AccountId = cr.AccountId,
                             AccountCode = cr.Account.AccountCode,
                             AccountName = cr.Account.AccountName,
                             Date = cr.GeneralLedgerHeader.Date,
                             Debit = (decimal)0,
                             Credit = cr.Amount,
                             //CurrencyId = cr.CurrencyId,
                             DocumentType = Enum.GetName(typeof(DocumentTypes), cr.GeneralLedgerHeader.DocumentType)
                         });

            var allDrcr = (from x in allDr
                           select new MasterGeneralLedger
                           {
                               Id = x.Id,
                               TransactionNo = x.TransactionNo,
                               AccountId = x.AccountId,
                               AccountCode = x.AccountCode,
                               AccountName = x.AccountName,
                               Date = x.Date,
                               Debit = x.Debit,
                               Credit = (decimal)0,
                               CurrencyId = x.CurrencyId,
                               DocumentType = x.DocumentType
                           }
                          ).Concat(from y in allCr
                                   select new MasterGeneralLedger
                                   {
                                       Id = y.Id,
                                       TransactionNo = y.TransactionNo,
                                       AccountId = y.AccountId,
                                       AccountCode = y.AccountCode,
                                       AccountName = y.AccountName,
                                       Date = y.Date,
                                       Debit = (decimal)0,
                                       Credit = y.Credit,
                                       CurrencyId = y.CurrencyId,
                                       DocumentType = y.DocumentType
                                   });

            if (!string.IsNullOrEmpty(accountCode))
                allDrcr = allDrcr.Where(a => a.AccountCode == accountCode);
            if (transactionNo != null)
                allDrcr = allDrcr.Where(a => a.TransactionNo == transactionNo);

            var sortedList = allDrcr.OrderBy(gl => gl.Id).ToList().Reverse<MasterGeneralLedger>();

            return sortedList.ToList();
        }

        public new IEnumerable<Bank> GetCashAndBanks()
        {
            var query = from b in _bankRepo.Table
                        select b;
            return query;
        }

        /// <summary>
        /// Calculates input tax charged on purchases.
        /// </summary>
        /// <param name="itemId"></param>
        /// <param name="quantity"></param>
        /// <param name="amount"></param>
        /// <returns></returns>
        public List<KeyValuePair<int, decimal>> ComputeInputTax(int vendorId, int itemId, decimal quantity, decimal amount, decimal discount)
        {
            decimal taxAmount, amountXquantity, discountAmount = 0, subTotalAmount;

            var taxes = new List<KeyValuePair<int, decimal>>();

            amountXquantity = amount * quantity;

            if (discount > 0)
                discountAmount = (discount / 100) * amountXquantity;

            subTotalAmount = amountXquantity - discountAmount;

            var intersectionTaxes = _taxService.GetIntersectionTaxes(itemId, vendorId, PartyTypes.Vendor);

            foreach (var tax in intersectionTaxes)
            {
                taxAmount = Math.Round(subTotalAmount - (subTotalAmount / (1 + (tax.Rate / 100))), 2, MidpointRounding.AwayFromZero);
                taxes.Add(new KeyValuePair<int, decimal>(tax.Id, taxAmount));
            }

            return taxes;
        }

        /// <summary>
        /// Calculates output tax charged on sales of goods and services.
        /// </summary>
        /// <param name="itemId"></param>
        /// <param name="quantity"></param>
        /// <param name="amount"></param>
        /// <returns></returns>
        public List<KeyValuePair<int, decimal>> ComputeOutputTax(int customerId, int itemId, decimal quantity, decimal amount, decimal discount)
        {
            decimal taxAmount, amountXquantity, discountAmount = 0, subTotalAmount;

            var taxes = new List<KeyValuePair<int, decimal>>();

            amountXquantity = amount * quantity;

            if (discount > 0)
                discountAmount = (discount / 100) * amountXquantity;

            subTotalAmount = amountXquantity - discountAmount;

            var intersectionTaxes = _taxService.GetIntersectionTaxes(itemId, customerId, PartyTypes.Customer);

            foreach (var tax in intersectionTaxes)
            {
                taxAmount = Math.Round(subTotalAmount - (subTotalAmount / (1 + (tax.Rate / 100))), 2, MidpointRounding.AwayFromZero);
                taxes.Add(new KeyValuePair<int, decimal>(tax.Id, taxAmount));
            }

            return taxes;
        }

        public GeneralLedgerSetting GetGeneralLedgerSetting()
        {
            return _glSettingRepo.Table.FirstOrDefault();
        }

        public void SaveGeneralLedgerSetting(GeneralLedgerSetting setting)
        {
            if (setting.Id == 0)
                _glSettingRepo.Insert(setting);
            else
                _glSettingRepo.Update(setting);
        }

        public void AddMainContraAccountSetting(int mainAccountId, int contraAccountId)
        {
            var contraAccount = _accountRepo.GetById(contraAccountId);
            var mainAccount = _accountRepo.GetById(mainAccountId);

            if (mainAccountId == contraAccountId)
                throw new Exception("Main account is same as contra account.");
            if (!contraAccount.IsContraAccount)
                throw new Exception("Account is not a contra account.");
            if (_maincontraAccount.Table.Any(a => a.MainAccountId == mainAccountId))
                throw new Exception("Main account already has contra account set.");

            _maincontraAccount.Insert(new MainContraAccount() { MainAccountId = mainAccountId, RelatedContraAccountId = contraAccountId });
        }

        public void UpdateAccount(Account account)
        {
            if (account.IsContraAccount && account.ContraAccounts.Count > 0)
                throw new Exception("An account cannot have contra account if the account is already contra account.");

            if (GetAccounts().Any(a => a.AccountCode == account.AccountCode && a.Id != account.Id))
                throw new Exception("Account code already exist.");

            if (account.ParentAccountId.HasValue && account.ParentAccountId.Value != -1)
            {
                var parent = GetAccount(account.ParentAccountId.Value);
                if (account.Id == parent.ParentAccountId)
                    throw new Exception("Cyclic parent/child account.");
            }

            _accountRepo.Update(account);
        }

        public void AddAccount(Account account)
        {
            if (account.IsContraAccount && account.ContraAccounts.Count > 0)
                throw new Exception("An account cannot have contra account if the account is already contra account.");

            if (GetAccounts().Any(a => a.AccountCode == account.AccountCode && a.Id != account.Id))
                throw new Exception("Account code already exist.");

            if (account.ParentAccountId.HasValue)
            {
                var parent = GetAccount(account.ParentAccountId.Value);
                if (account.Id == parent.ParentAccountId)
                    throw new Exception("Cyclic parent/child account.");
            }

            _accountRepo.Insert(account);
        }

        public JournalEntryHeader GetJournalEntry(int id, bool fromGL = false)
        {
            if (fromGL)
            {
                var je = _journalEntryRepo.GetAllIncluding(j => j.GeneralLedgerHeader,
                    j => j.JournalEntryLines,
                    j => j.Party)
                    .Where(j => j.GeneralLedgerHeaderId == id)
                    .FirstOrDefault();
                return je;
            }
            else
            {
                var je = _journalEntryRepo.GetAllIncluding(j => j.GeneralLedgerHeader,
                    j => j.JournalEntryLines,
                    j => j.Party)
                    .Where(j => j.Id == id)
                    .FirstOrDefault();
                return je;
            }
        }

        public void UpdateJournalEntry(JournalEntryHeader journalEntry, bool posted = false)
        {
            if (journalEntry == null)
                throw new ArgumentNullException(nameof(journalEntry));
            if (journalEntry.Posted == true || journalEntry.GeneralLedgerHeaderId.HasValue)
                throw new InvalidOperationException("Posted journal entries cannot be edited or posted again.");

            if (posted)
            {
                if (journalEntry.JournalEntryLines.Count < 2
                    || journalEntry.JournalEntryLines.Any(l => l.Amount <= 0
                        || (l.DrCr != DrOrCrSide.Dr && l.DrCr != DrOrCrSide.Cr)))
                    throw new InvalidOperationException("Posting requires at least two positive debit or credit lines.");

                if (journalEntry.GeneralLedgerHeaderId == null || journalEntry.GeneralLedgerHeaderId == 0)
                {
                    EnsureOnOrAfterBooksStart(journalEntry.Date);
                    var glEntry = new GeneralLedgerHeader()
                    {
                        Date = journalEntry.Date,
                        DocumentType = DocumentTypes.JournalEntry,
                        Description = journalEntry.Memo,
                        SourceDocumentId = journalEntry.Id > 0 ? journalEntry.Id : null,
                    };

                    foreach (var je in journalEntry.JournalEntryLines)
                    {
                        glEntry.GeneralLedgerLines.Add(new GeneralLedgerLine()
                        {
                            Account = GetAccounts().Where(a => a.Id == je.AccountId).FirstOrDefault(),
                            AccountId = je.AccountId,
                            DrCr = je.DrCr,
                            Amount = je.Amount,
                        });
                    }

                    if (ValidateGeneralLedgerEntry(glEntry))
                    {
                        journalEntry.GeneralLedgerHeader = glEntry;
                        journalEntry.Posted = true;
                    }
                }
            }

            _journalEntryRepo.Update(journalEntry);
        }

        public GeneralLedgerHeader GetGeneralLedgerHeader(int id)
        {
            return _generalLedgerRepository.Table.Where(gl => gl.Id == id).FirstOrDefault();
        }

        public IEnumerable<GeneralLedgerLine> GetGeneralLedgerLines(int generalLedgerHeaderId) =>
            _generalLedgerRepository.GetAllIncluding(gl => gl.GeneralLedgerLines)
                .Where(gl => gl.Id == generalLedgerHeaderId)
                .SelectMany(gl => gl.GeneralLedgerLines)
                .ToList();

        /// <summary>
        /// An entry that undoes another, not yet saved: the same lines with debits and credits swapped, dated as the
        /// original so a voided document nets to nothing in its own period. When that period's financial year is no
        /// longer open, it is dated today instead.
        /// </summary>
        public GeneralLedgerHeader CreateReversingEntry(int generalLedgerHeaderId, string description)
        {
            var original = _generalLedgerRepository.GetAllIncluding(gl => gl.GeneralLedgerLines)
                .FirstOrDefault(gl => gl.Id == generalLedgerHeaderId)
                ?? throw new InvalidOperationException($"Journal entry {generalLedgerHeaderId} was not found.");

            var date = _fiscalYearRepo.Table.Any(fy => fy.IsActive && original.Date >= fy.StartDate && original.Date <= fy.EndDate)
                ? original.Date
                : DateTime.UtcNow.Date;
            var reversal = CreateGeneralLedgerHeader(original.DocumentType, date, description, original.SourceDocumentId, original.Id);
            foreach (var line in original.GeneralLedgerLines)
                reversal.GeneralLedgerLines.Add(CreateGeneralLedgerLine(line.DrCr == DrOrCrSide.Dr ? DrOrCrSide.Cr : DrOrCrSide.Dr, line.AccountId, line.Amount));

            ValidateGeneralLedgerEntry(reversal);
            return reversal;
        }

        public JournalEntryHeader CloseAccountingPeriod()
        {
            /*
            Example:

            The following example shows the closing entries based on the adjusted trial balance of Company A.

            Note	
            
            Date	        Account	                Debit	        Credit
            1	Jan 31	    Service Revenue	        85,600	
                            Income Summary		                    85,600
            2	Jan 31	    Income Summary	        77,364	
                            Wages Expense		                    38,200
                            Supplies Expense	                    18,480
                            Rent Expense		                    12,000
                            Miscellaneous Expense	                3,470
                            Electricity Expense		                2,470
                            Telephone Expense		                1,494
                            Depreciation Expense	                1,100
                            Interest Expense		                150
            3	Jan 31	    Income Summary	        8,236	
                            Retained Earnings		                8,236
            4	Jan 31	    Retained Earnings	    5,000	
                            Dividend		                        5,000
            
            Notes

            1. Service revenue account is debited and its balance it credited to income summary account. If a business has other income accounts, for example gain on sale account, then the debit side of the first closing entry will also include the gain on sale account and the income summary account will be credited for the sum of all income accounts.
            2. Each expense account is credited and the income summary is debited for the sum of the balances of expense accounts. This will reduce the balance in income summary account.
            3. Income summary account is debited and retained earnings account is credited for the an amount equal to the excess of service revenue over total expenses i.e. the net balance in income summary account after posting the first two closing entries. In this case $85,600 − $77,364 = $8,236. Please note that, if the balance in income summary account is negative at this stage, this closing entry will be opposite i.e. debit to retained earnings and credit to income summary.
            4. The last closing entry transfers the dividend or withdrawal account balance to the retained earnings account. Since dividend and withdrawal accounts are contra to the retained earnings account, they reduce the balance in the retained earnings.
            */

            var glSetting = _generalLedgerSettingRepo.Table.FirstOrDefault();

            var journalEntry = new JournalEntryHeader()
            {
                Memo = "Closing entries",
                Date = DateTime.Now,
                Posted = false,
                VoucherType = JournalVoucherTypes.ClosingEntries
            };

            return journalEntry;
        }

        public void SaveAccountClasses(IList<AccountClass> accountClasses)
        {
            foreach (var accountClass in accountClasses)
            {
                if (accountClass.Id == 0)
                    _accountClassRepo.Insert(accountClass);
                else
                    _accountClassRepo.Update(accountClass);
            }
        }

        public void SaveFinancialYear(FinancialYear financialYear)
        {
            if (financialYear.Id == 0)
                _fiscalYearRepo.Insert(financialYear);
            else
                _fiscalYearRepo.Update(financialYear);
        }

        public void SavePaymentTerm(PaymentTerm paymentTerm)
        {
            if (paymentTerm.Id == 0)
                _paymentTermRepo.Insert(paymentTerm);
            else
                _paymentTermRepo.Update(paymentTerm);
        }

        public void SaveBank(Bank bank)
        {
            if (bank.Id == 0)
                _bankRepo.Insert(bank);
            else
                _bankRepo.Update(bank);
        }

        public Bank GetBank(int id)
        {
            return _bankRepo.GetById(id);
        }

        public void DeleteBank(int id)
        {
            var bank = _bankRepo.GetById(id);
            if (bank != null)
                _bankRepo.Delete(bank);
        }
    }
}
