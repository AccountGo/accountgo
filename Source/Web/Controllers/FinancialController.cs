//-----------------------------------------------------------------------
// <copyright file="FinancialController.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Services.Financial;
using System.Web.Mvc;
using System;
using System.Linq;
using Core.Domain.Financials;
using System.IO;

namespace Web.Controllers
{
    [Authorize]
    public class FinancialController : BaseController
    {
        private readonly IFinancialService _financialService;

        public FinancialController(IFinancialService financialService)
        {
            _financialService = financialService;
        }

        public ActionResult Accounts()
        {
            var accounts = _financialService.GetAccounts();
            var model = new Models.ViewModels.Financials.Accounts();
            foreach(var account in accounts)
            {
                model.AccountsListLines.Add(new Models.ViewModels.Financials.AccountsListLine()
                {
                    Id = account.Id,
                    AccountCode = account.AccountCode,
                    AccountName = account.AccountName,
                    Balance = account.Balance
                });
            }
            return View(model);
        }


        public ActionResult ViewAccountsPDF()
        {
            var accounts = _financialService.GetAccounts();
            var model = new Models.ViewModels.Financials.Accounts();
            foreach (var account in accounts)
            {
                model.AccountsListLines.Add(new Models.ViewModels.Financials.AccountsListLine()
                {
                    Id = account.Id,
                    AccountCode = account.AccountCode,
                    AccountName = account.AccountName,
                    Balance = account.Balance
                });
            }

            var html = base.RenderPartialViewToString("Accounts", model);
            HttpContext.Response.Clear();
            HttpContext.Response.AddHeader("Content-Type", "application/pdf");
            HttpContext.Response.Filter = new PdfFilter(HttpContext.Response.Filter, html);

            return Content(html);
        }

        public ActionResult JournalEntries()
        {
            var journalEntries = _financialService.GetJournalEntries();
            var model = new Models.ViewModels.Financials.JournalEntries();
            foreach(var je in journalEntries)
            {
                foreach (var line in je.JournalEntryLines)
                {
                    model.JournalEntriesListLines.Add(new Models.ViewModels.Financials.JournalEntriesListLine()
                    {
                        AccountId = line.AccountId,
                        AccountCode = line.Account.AccountCode,
                        AccountName = line.Account.AccountName,
                        DrCr = (int)line.DrCr == 1 ? "Dr" : "Cr",
                        Amount = line.Amount
                    });
                }
            }
            return View(model);
        }

        public ActionResult AddJournalEntry()
        {
            var model = new Models.ViewModels.Financials.AddJournalEntry();
            return View(model);
        }

        [HttpPost, ActionName("AddJournalEntry")]
        [FormValueRequiredAttribute("AddJournalEntryLine")]
        public ActionResult AddJournalEntryLine(Models.ViewModels.Financials.AddJournalEntry model)
        {
            if(model.AccountId != -1 && model.Amount > 0)
            {
                var rowId = Guid.NewGuid().ToString();
                model.AddJournalEntryLines.Add(new Models.ViewModels.Financials.AddJournalEntryLine()
                {
                    RowId = rowId,
                    AccountId = model.AccountId,
                    AccountName = _financialService.GetAccounts().Where(a => a.Id == model.AccountId).FirstOrDefault().AccountName,
                    DrCr = model.DrCr,
                    Amount = model.Amount,
                    Memo = model.MemoLine
                });
            }
            return View(model);
        }

        [HttpPost, ActionName("AddJournalEntry")]
        [FormValueRequiredAttribute("DeleteJournalEntryLine")]
        public ActionResult DeleteJournalEntryLine(Models.ViewModels.Financials.AddJournalEntry model)
        {
            var request = HttpContext.Request;
            var deletedItem = request.Form["DeletedLineItem"];
            model.AddJournalEntryLines.Remove(model.AddJournalEntryLines.Where(i => i.RowId.ToString() == deletedItem.ToString()).FirstOrDefault());
            return View(model);
        }

        [HttpPost, ActionName("AddJournalEntry")]
        [FormValueRequiredAttribute("SaveJournalEntry")]
        public ActionResult SaveJournalEntry(Models.ViewModels.Financials.AddJournalEntry model)
        {
            if(model.AddJournalEntryLines.Count < 2)
                return View(model);
            var journalEntry = new JournalEntryHeader()
            {
                Date = model.Date,
                Memo = model.Memo,
                ReferenceNo = model.ReferenceNo,
                CreatedBy = User.Identity.Name,
                CreatedOn = DateTime.Now,
                ModifiedBy = User.Identity.Name,
                ModifiedOn = DateTime.Now,
            };
            foreach(var line in model.AddJournalEntryLines)
            {
                journalEntry.JournalEntryLines.Add(new JournalEntryLine()
                {
                    AccountId = line.AccountId,
                    DrCr = line.DrCr,
                    Amount = line.Amount,
                    Memo = line.Memo
                });
            }
            _financialService.AddJournalEntry(journalEntry);
            return RedirectToAction("JournalEntries");
        }

        public ActionResult MasterGeneralLedger()
        {
            var model = _financialService.MasterGeneralLedger();
            return View(model);
        }

        public ActionResult TrialBalance()
        {
            var model = _financialService.TrialBalance();
            return View(model);
        }

        public ActionResult BalanceSheet()
        {
            var model = _financialService.BalanceSheet().ToList();
            var dt = Helpers.CollectionHelper.ConvertTo<BalanceSheet>(model);
            var incomestatement = _financialService.IncomeStatement();
            var netincome = incomestatement.Where(a => a.IsExpense == false).Sum(a => a.Amount) - incomestatement.Where(a => a.IsExpense == true).Sum(a => a.Amount);

            // TODO: Add logic to get the correct account for accumulated profit/loss. Currently, the account code is hard-coded here.
            // Solution 1: Add two columns in general ledger setting for the account id of accumulated profit and loss.
            // Solution 2: Add column to Account table to flag if account is net income (profit and loss)
            if (netincome < 0)
            {
                var loss = model.Where(a => a.AccountCode == "30500").FirstOrDefault();
                loss.Amount = netincome;
            }
            else
            {
                var profit = model.Where(a => a.AccountCode == "30400").FirstOrDefault();
                profit.Amount = netincome;
            }

            return View(model);
        }

        public ActionResult IncomeStatement()
        {
            var model = _financialService.IncomeStatement();
            return View(model);
        }

        public ActionResult Banks()
        {
            var model = new Models.ViewModels.Financials.Banks();
            var banks = _financialService.GetCashAndBanks();
            foreach (var bank in banks)
            {
                model.BankList.Add(new Models.ViewModels.Financials.BankListLine()
                {
                    Name = bank.Name,
                    BankName = bank.BankName,
                    AccountId = bank.AccountId,
                    Number = bank.Number,
                    Type = bank.Type,
                    Address = bank.Address,
                    IsActive = bank.IsActive,
                    IsDefault = bank.IsDefault
                });
            }
            return View(model);
        }

        public ActionResult GeneralLedgerSetting()
        {
            var glSetting = _financialService.GetGeneralLedgerSetting();
            var model = new Models.ViewModels.Financials.GeneralLedgerSettingViewModel();
            model.Id = glSetting.Id;
            model.CreatedBy = glSetting.CreatedBy;
            model.CreatedOn = glSetting.CreatedOn;
            model.ModifiedBy = glSetting.ModifiedBy;
            model.ModifiedOn = glSetting.ModifiedOn;
            model.CompanyId = glSetting.CompanyId;
            model.PayableAccountId = glSetting.PayableAccountId;
            model.PurchaseDiscountAccountId = glSetting.PurchaseDiscountAccountId;
            model.SalesDiscountAccountId = glSetting.SalesDiscountAccountId;
            model.ShippingChargeAccountId = glSetting.ShippingChargeAccountId;
            model.GoodsReceiptNoteClearingAccountId = glSetting.GoodsReceiptNoteClearingAccountId;
            return View(model);
        }

        [HttpPost, ActionName("GeneralLedgerSetting")]
        [FormValueRequiredAttribute("Save")]
        public ActionResult GeneralLedgerSetting(Models.ViewModels.Financials.GeneralLedgerSettingViewModel model)
        {
            var glSetting = _financialService.GetGeneralLedgerSetting();
            glSetting.ModifiedBy = User.Identity.Name;
            glSetting.ModifiedOn = DateTime.Now;
            glSetting.CompanyId = model.CompanyId;
            glSetting.PayableAccountId = model.PayableAccountId;
            glSetting.PurchaseDiscountAccountId = model.PurchaseDiscountAccountId;
            glSetting.SalesDiscountAccountId = model.SalesDiscountAccountId;
            glSetting.ShippingChargeAccountId = model.ShippingChargeAccountId;
            glSetting.GoodsReceiptNoteClearingAccountId = model.GoodsReceiptNoteClearingAccountId;
            _financialService.UpdateGeneralLedgerSetting(glSetting);
            return View(model);
        }
    }
}
