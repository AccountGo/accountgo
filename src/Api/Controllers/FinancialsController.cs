using Dto.Financial;
using Microsoft.AspNetCore.Mvc;
using Services.Administration;
using Services.Financial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class FinancialsController : BaseController
    {
        private readonly IAdministrationService _adminService;
        private readonly IFinancialService _financialService;

        public FinancialsController(IAdministrationService adminService,
            IFinancialService financialService)
        {
            _adminService = adminService;
            _financialService = financialService;
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult CashBanks()
        {
            var cashAndBanks = _financialService.GetCashAndBanks();
            var cashAndBanksDto = new List<Bank>();

            foreach (var bank in cashAndBanks) {
                cashAndBanksDto.Add(new Bank()
                {
                    Id = bank.Id,
                    Name = bank.Name,
                    AccountNo = bank.Number,
                    BankName = bank.BankName
                });
            }

            return new ObjectResult(cashAndBanksDto);
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Accounts()
        {
            var accounts = _financialService.GetAccounts().ToList();

            var accountTree = BuildAccountGrouping(accounts, null);

            return new ObjectResult(accountTree);
        }

        [Route("[action]")]
        public IActionResult Account(int id)
        {
            var account = _financialService.GetAccount(id);

            var accountDto = new Dto.Financial.Account()
            {
                Id = account.Id,
                AccountClassId = account.AccountClassId,
                ParentAccountId = account.ParentAccountId,
                CompanyId = account.CompanyId,
                AccountCode = account.AccountCode,
                AccountName = account.AccountName,
                Description = account.Description,
                IsCash = account.IsCash,
                IsContraAccount = account.IsContraAccount,
                Balance = account.Balance,
                DebitBalance = account.DebitBalance,
                CreditBalance = account.CreditBalance
            };

            return new ObjectResult(accountDto);
        }


        [HttpGet]
        [Route("[action]")]
        public IActionResult JournalEntries()
        {
            var journalEntries = _financialService.GetJournalEntries();
            var journalEntriesDto = new List<JournalEntry>();

            foreach (var je in journalEntries) {
                var journalEntryDto = new JournalEntry()
                {
                    Id = je.Id,
                    JournalDate = je.Date,
                    Memo = je.Memo,
                    ReferenceNo = je.ReferenceNo,
                    VoucherType = (int)je.VoucherType.GetValueOrDefault(),
                    Posted = je.Posted
                };

                foreach (var line in je.JournalEntryLines) {
                    var lineDto = new JournalEntryLine()
                    {
                        Id = line.Id,
                        AccountId = line.AccountId,
                        Amount = line.Amount,
                        DrCr = (int)line.DrCr,
                        Memo = line.Memo
                    };

                    journalEntryDto.JournalEntryLines.Add(lineDto);
                }

                journalEntriesDto.Add(journalEntryDto);
            }

            return new ObjectResult(journalEntriesDto);
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult JournalEntry(int id)
        {
            var je = _financialService.GetJournalEntry(id);

            var journalEntryDto = new JournalEntry()
            {
                Id = je.Id,
                JournalDate = je.Date,
                Memo = je.Memo,
                ReferenceNo = je.ReferenceNo,
                VoucherType = (int)je.VoucherType.GetValueOrDefault(),
                Posted = je.Posted
            };

            foreach (var line in je.JournalEntryLines)
            {
                var lineDto = new JournalEntryLine()
                {
                    Id = line.Id,
                    AccountId = line.AccountId,
                    Amount = line.Amount,
                    DrCr = (int)line.DrCr,
                    Memo = line.Memo
                };

                journalEntryDto.JournalEntryLines.Add(lineDto);
            }

            // is this journal entry ready for posting?
            if (!journalEntryDto.Posted.GetValueOrDefault()
                && journalEntryDto.JournalEntryLines.Count >= 2
                && (journalEntryDto.debitAmount == journalEntryDto.creditAmount))
            {
                journalEntryDto.ReadyForPosting = true;
            }

            return new ObjectResult(journalEntryDto);
        }

        [HttpPost]
        [Route("[action]")]
        public IActionResult PostJournalEntry([FromBody]JournalEntry journalEntryDto)
        {
            string[] errors = null;

            try
            {
                if (!ModelState.IsValid)
                {
                    errors = new string[ModelState.ErrorCount];
                    foreach (var val in ModelState.Values)
                        for (int i = 0; i < ModelState.ErrorCount; i++)
                            errors[i] = val.Errors[i].ErrorMessage;

                    return new BadRequestObjectResult(errors);
                }

                var je = _financialService.GetJournalEntry(journalEntryDto.Id, false);

                _financialService.UpdateJournalEntry(je, true);

                return new OkObjectResult(Ok());
            }
            catch (Exception ex)
            {
                errors = new string[1] { ex.InnerException != null ? ex.InnerException.Message : ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }

        [HttpPost]
        [Route("[action]")]
        public IActionResult SaveJournalEntry([FromBody]JournalEntry journalEntryDto)
        {
            string[] errors = null;

            try
            {
                if (!ModelState.IsValid)
                {
                    errors = new string[ModelState.ErrorCount];
                    foreach (var val in ModelState.Values)
                        for (int i = 0; i < ModelState.ErrorCount; i++)
                            errors[i] = val.Errors[i].ErrorMessage;

                    return new BadRequestObjectResult(errors);
                }

                var anyDuplicate = journalEntryDto.JournalEntryLines.GroupBy(x => x.AccountId).Any(g => g.Count() > 1);
                if (anyDuplicate)
                    throw new Exception("One or more journal entry lines has duplicate account.");

                bool isNew = journalEntryDto.Id == 0;
                Core.Domain.Financials.JournalEntryHeader journalEntry = null;

                if (isNew)
                {
                    journalEntry = new Core.Domain.Financials.JournalEntryHeader();
                }
                else
                {
                    journalEntry = _financialService.GetJournalEntry(journalEntryDto.Id, false);
                }

                journalEntry.Date = journalEntryDto.JournalDate;
                journalEntry.VoucherType = (Core.Domain.JournalVoucherTypes)journalEntryDto.VoucherType.GetValueOrDefault();
                journalEntry.ReferenceNo = journalEntryDto.ReferenceNo;
                journalEntry.Memo = journalEntryDto.Memo;

                foreach (var line in journalEntryDto.JournalEntryLines)
                {
                    if (!isNew)
                    {
                        var existingLine = journalEntry.JournalEntryLines
                            .Where(j => j.Id == line.Id && line.Id != 0)
                            .FirstOrDefault();

                        if (existingLine != null)
                        {
                            existingLine.AccountId = line.AccountId.GetValueOrDefault();
                            existingLine.DrCr = (Core.Domain.DrOrCrSide)line.DrCr;
                            existingLine.Amount = line.Amount.GetValueOrDefault();
                            existingLine.Memo = line.Memo;
                        }
                        else
                        {
                            var journalLine = new Core.Domain.Financials.JournalEntryLine();
                            journalLine.AccountId = line.AccountId.GetValueOrDefault();
                            journalLine.DrCr = (Core.Domain.DrOrCrSide)line.DrCr;
                            journalLine.Amount = line.Amount.GetValueOrDefault();
                            journalLine.Memo = line.Memo;
                            journalEntry.JournalEntryLines.Add(journalLine);
                        }
                    }
                    else
                    {
                        var journalLine = new Core.Domain.Financials.JournalEntryLine();
                        journalLine.AccountId = line.AccountId.GetValueOrDefault();
                        journalLine.DrCr = (Core.Domain.DrOrCrSide)line.DrCr;
                        journalLine.Amount = line.Amount.GetValueOrDefault();
                        journalLine.Memo = line.Memo;
                        journalEntry.JournalEntryLines.Add(journalLine);
                    }
                }

                if (isNew)
                {
                    _financialService.AddJournalEntry(journalEntry);
                }
                else
                {
                    _financialService.UpdateJournalEntry(journalEntry, false);
                }

                return new OkObjectResult(Ok());
            }
            catch (Exception ex)
            {
                errors = new string[1] { ex.InnerException != null ? ex.InnerException.Message : ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }

        [HttpGet]
        [Route("[action]")]
        public ActionResult GeneralLedger(DateTime? from = default(DateTime?),
            DateTime? to = default(DateTime?),
            string accountCode = null,
            int? transactionNo = null)
        {
            var Dto = _financialService.MasterGeneralLedger(from, to, accountCode, transactionNo);

            //goi
            var generalLedgerTree = BuildMasterGeneralLedger(Dto);

            return new ObjectResult(generalLedgerTree);
 
        }

        [HttpGet]
        [Route("[action]")]
        public ActionResult TrialBalance()
        {
            var Dto = _financialService.TrialBalance();
            return new ObjectResult(Dto);
        }

        [HttpGet]
        [Route("[action]")]
        public ActionResult BalanceSheet()
        {
            var Dto = _financialService.BalanceSheet().ToList();
            var dt = Helpers.CollectionHelper.ConvertTo<BalanceSheet>(Dto);
            var incomestatement = _financialService.IncomeStatement();
            var netincome = incomestatement.Where(a => a.IsExpense == false).Sum(a => a.Amount) - incomestatement.Where(a => a.IsExpense == true).Sum(a => a.Amount);

            // TODO: Add logic to get the correct account for accumulated profit/loss. Currently, the account code is hard-coded here.
            // Solution 1: Add two columns in general ledger setting for the account id of accumulated profit and loss.
            // Solution 2: Add column to Account table to flag if account is net income (profit and loss)
            //if (netincome < 0)
            //{
            //    var loss = Dto.Where(a => a.AccountCode == "30500").FirstOrDefault();
            //    loss.Amount = netincome;
            //}
            //else
            //{
            //    var profit = Dto.Where(a => a.AccountCode == "30400").FirstOrDefault();
            //    profit.Amount = netincome;
            //}

            return new ObjectResult(Dto);
        }

        [HttpGet]
        [Route("[action]")]
        public ActionResult IncomeStatement()
        {
            var Dto = _financialService.IncomeStatement();
            return new ObjectResult(Dto);
        }

        #region Private Methods
        private IList<Dto.Financial.Account> BuildAccountGrouping(IList<Core.Domain.Financials.Account> allAccounts,
                                          int? parentAccountId)
        {
            var accountTree = new List<Dto.Financial.Account>();
            var childAccounts = allAccounts.Where(o => o.ParentAccountId == parentAccountId).ToList();

            foreach (var account in childAccounts)
            {
                var accountDto = new Dto.Financial.Account()
                {
                    Id = account.Id,
                    AccountClassId = account.AccountClassId,
                    ParentAccountId = account.ParentAccountId,
                    CompanyId = account.CompanyId,
                    AccountCode = account.AccountCode,
                    AccountName = account.AccountName,
                    Description = account.Description,
                    IsCash = account.IsCash,
                    IsContraAccount = account.IsContraAccount,
                    Balance = account.Balance,
                    DebitBalance = account.DebitBalance,
                    CreditBalance = account.CreditBalance
                };
                var children = BuildAccountGrouping(allAccounts, account.Id);
                accountDto.ChildAccounts = children;
                accountTree.Add(accountDto);
            }

            return accountTree;
        }


        private IList<Dto.Financial.MasterGeneralLedger> BuildMasterGeneralLedger(ICollection<Services.Financial.MasterGeneralLedger> allLedger)
        {
            var ledgerTree = new List<Dto.Financial.MasterGeneralLedger>();

 
            var parentLedger = allLedger.Select(a => a.TransactionNo).Distinct();
            var childLedgers = new List<Dto.Financial.MasterGeneralLedger>();
            parentLedger.ToList().ForEach(a =>
            {
                var childrenLedger = allLedger.Where(x => x.TransactionNo == a);

                var secondChild = new Dto.Financial.MasterGeneralLedger();
                var thirdChildren = new List<Dto.Financial.MasterGeneralLedger>();
                secondChild.GroupId = a;
                secondChild.TransactionNo = null;
                secondChild.Credit = null;
                secondChild.Debit = null;
                secondChild.Date = null;
                foreach (var ledger in childrenLedger)
                {
                        var thirdChild = new Dto.Financial.MasterGeneralLedger();
                        thirdChild.Id = ledger.Id;
                        thirdChild.TransactionNo = ledger.TransactionNo;
                        thirdChild.AccountId = ledger.AccountId;
                        thirdChild.AccountName = ledger.AccountName;
                        thirdChild.AccountCode = ledger.AccountCode;
                        thirdChild.CurrencyId = ledger.CurrencyId;
                        thirdChild.Date = ledger.Date;
                        thirdChild.Debit = ledger.Debit;
                        thirdChild.Credit = ledger.Credit;
                        thirdChildren.Add(thirdChild);
                        secondChild.ChildMasterGeneralLedger = thirdChildren;                    
                }

                childLedgers.Add(secondChild);


            });

            return childLedgers;
        }




        #endregion
    }
}