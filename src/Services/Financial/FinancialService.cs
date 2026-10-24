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
            IRepository<MainContraAccount> maincontraAccount = null,
            IRepository<Customer> customerRepo = null,
            IRepository<Vendor> vendorRepo = null,
            IRepository<GeneralLedgerSetting> generalLedgerSettingRepo = null
            )
            : base(null, null, paymentTermRepo, bankRepo)
        {
            _inventoryService = inventoryService;
            _taxService = taxService;
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
        }

        public FinancialYear CurrentFiscalYear()
        {
            var query = (from fy in _fiscalYearRepo.Table
                         where fy.IsActive == true
                         select fy).FirstOrDefault();

            return query;
        }

        public GeneralLedgerHeader CreateGeneralLedgerHeader(DocumentTypes documentType, DateTime date, string description)
        {
            var entry = new GeneralLedgerHeader()
            {
                DocumentType = documentType,
                Date = date,
                Description = description,
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

        public ICollection<TrialBalance> TrialBalance(DateTime? from = default(DateTime?), DateTime? to = default(DateTime?))
        {
            if (from.HasValue && to.HasValue && from.Value > to.Value)
                throw new ArgumentException("The start date must not be after the end date.");

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

        public ICollection<BalanceSheet> BalanceSheet(DateTime? from = default(DateTime?), DateTime? to = default(DateTime?))
        {
            if (from.HasValue && to.HasValue && from.Value > to.Value)
                throw new ArgumentException("The start date must not be after the end date.");

            // A position statement includes opening balances, not just movement since 'from'.
            var balances = TrialBalance(null, to).ToDictionary(a => a.AccountId);
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

        public ICollection<IncomeStatement> IncomeStatement(DateTime? from = default(DateTime?), DateTime? to = default(DateTime?))
        {
            var balances = TrialBalance(from, to).ToDictionary(a => a.AccountId);
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
            if (posted)
            {
                journalEntry.Posted = posted;

                if (journalEntry.GeneralLedgerHeaderId == null || journalEntry.GeneralLedgerHeaderId == 0)
                {
                    var glEntry = new GeneralLedgerHeader()
                    {
                        Date = DateTime.Now,
                        DocumentType = DocumentTypes.JournalEntry,
                        Description = journalEntry.Memo,
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
            var reversal = CreateGeneralLedgerHeader(original.DocumentType, date, description);
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
