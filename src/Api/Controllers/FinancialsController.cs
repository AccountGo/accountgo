using Microsoft.AspNetCore.Mvc;
using Dto.Financial;
using Services.Administration;
using Services.Financial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class FinancialsController : Controller
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
        public IActionResult Accounts()
        {
            var accounts = _financialService.GetAccounts();
            var accountsDto = new System.Collections.Generic.List<Dto.Financial.Account>();

            foreach (var account in accounts)
            {
                var accountDto = new Dto.Financial.Account()
                {
                    Id = account.Id,
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

                accountsDto.Add(accountDto);
            }

            return new ObjectResult(accountsDto);
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult JournalEntries()
        {
            var journalEntries = _financialService.GetJournalEntryLines();
            var Dto = new List<JournalEntryLine>();
            foreach (var line in journalEntries)
            {
                Dto.Add(new JournalEntryLine()
                {
                    Id = line.Id,
                    AccountId = line.AccountId,
                    AccountCode = line.Account.AccountCode,
                    AccountName = line.Account.AccountName,
                    DrCr = (int)line.DrCr == 1 ? "Dr" : "Cr",
                    Amount = line.Amount,
                    JournalHeaderId = line.JournalEntryHeaderId,
                    GeneralLedgerHeaderId = line.JournalEntryHeader.GeneralLedgerHeaderId.HasValue ? line.JournalEntryHeader.GeneralLedgerHeaderId.Value : 0
                });
            }
            return new OkObjectResult(Dto.AsEnumerable());
        }

        [HttpPost]
        [Route("[action]")]
        public IActionResult AddJournalEntry([FromBody]JournalEntry journalEntryDto)
        {
            try
            {
                return new ObjectResult(null);
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex);
            }
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult GeneralLedger(DateTime? from = default(DateTime?),
            DateTime? to = default(DateTime?),
            string accountCode = null,
            int? transactionNo = null)
        {
            var Dto = _financialService.MasterGeneralLedger(from, to, accountCode, transactionNo);
            return new OkObjectResult(Dto.AsEnumerable());
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
    }
}
