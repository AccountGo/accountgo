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
using Web.Models.ViewModels.Financials;
using Services.Administration;
using System.Collections.Generic;
using Web.Models.ViewModels.Administration;

namespace Web.Controllers
{
    [Authorize]
    public class FinancialController : BaseController
    {
        private readonly IFinancialService _financialService;
        private readonly IAdministrationService _administrationService;

        public FinancialController(IFinancialService financialService, IAdministrationService administrationService)
        {
            _financialService = financialService;
            _administrationService = administrationService;
        }

        public ActionResult Accounts()
        {
            var accounts = _financialService.GetAccounts();
            var model = new Accounts();
            foreach(var account in accounts)
            {
                model.AccountsListLines.Add(new AccountsListLine()
                {
                    Id = account.Id,
                    AccountCode = account.AccountCode,
                    AccountName = account.AccountName,
                    Balance = account.Balance,
                    DebitBalance = account.DebitBalance,
                    CreditBalance = account.CreditBalance
                });
            }
            return View(model);
        }

        public ActionResult AddAccount()
        {
            return View(new AddAccountViewModel());
        }

        [HttpPost, ActionName("AddAccount")]
        [FormValueRequiredAttribute("Create")]
        public ActionResult AddAccount(AddAccountViewModel model)
        {
            Account account = new Account()
            {
                AccountCode = model.AccountCode,
                AccountName = model.AccountName,
                AccountClassId = model.AccountClass,
                ParentAccountId = model.ParentAccountId == -1 ? null : model.ParentAccountId,
                CompanyId = _administrationService.GetDefaultCompany().Id,
            };

            _financialService.AddAccount(account);

            return RedirectToAction("Accounts");
        }

        public ActionResult EditAccount(int id)
        {
            var account = _financialService.GetAccounts().Where(a => a.Id == id).FirstOrDefault();

            Models.ViewModels.Financials.EditAccountViewModel model = new Models.ViewModels.Financials.EditAccountViewModel()
            {
                Id = account.Id,
                AccountCode = account.AccountCode,
                AccountName = account.AccountName,
                AccountClass = account.AccountClass.Name,
                IsContraAccount = account.IsContraAccount,
                Balance = account.Balance,
                ParentAccountId = account.ParentAccountId
            };

            model.Transactions = _financialService.MasterGeneralLedger(null, null, model.AccountCode);

            return View(model);
        }

        [HttpPost, ActionName("EditAccount")]
        [FormValueRequiredAttribute("Save")]
        public ActionResult EditAccount(Models.ViewModels.Financials.EditAccountViewModel model)
        {
            var account = _financialService.GetAccounts().Where(a => a.Id == model.Id).FirstOrDefault();

            account.IsContraAccount = model.IsContraAccount;
            account.AccountCode = model.AccountCode;
            account.AccountName = model.AccountName;
            account.ParentAccountId = model.ParentAccountId.Value == -1 ? null: model.ParentAccountId;

            _financialService.UpdateAccount(account);

            return RedirectToAction("Accounts");
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
                        Id = line.Id,
                        AccountId = line.AccountId,
                        AccountCode = line.Account.AccountCode,
                        AccountName = line.Account.AccountName,
                        DrCr = (int)line.DrCr == 1 ? "Dr" : "Cr",
                        Amount = line.Amount,
                        JournalHeaderId = line.JournalEntryHeaderId,
                        GeneralLedgerHeaderId = je.GeneralLedgerHeaderId.HasValue ? je.GeneralLedgerHeaderId.Value : 0
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

        public ActionResult EditJournalEntry(int id, bool fromGL = false)
        {
            // for now, use the same view model as add journal entry. nothing different
            var je = _financialService.GetJournalEntry(id, fromGL);

            var model = new Models.ViewModels.Financials.AddJournalEntry();
            model.Date = je.Date;
            model.Memo = je.Memo;
            model.ReferenceNo = je.ReferenceNo;
            model.Id = je.Id;
            model.JournalEntryId = je.Id;
            model.Posted = je.Posted.HasValue ? je.Posted.Value : false;

            foreach (var line in je.JournalEntryLines)
            {
                model.AddJournalEntryLines.Add(new Models.ViewModels.Financials.AddJournalEntryLine()
                {
                    RowId = line.Id.ToString(),
                    AccountId = line.AccountId,
                    AccountName = line.Account.AccountName,
                    DrCr = line.DrCr,
                    Amount = line.Amount,
                    Memo = line.Memo
                });
            }

            return View(model);
        }

        [HttpPost, ActionName("EditJournalEntry")]
        [FormValueRequiredAttribute("UpdateJournalEntry")]
        public ActionResult EditJournalEntry(Models.ViewModels.Financials.AddJournalEntry model)
        {
            if (model.AddJournalEntryLines.Count < 2)
                return View(model);

            var journalEntry = _financialService.GetJournalEntry(model.JournalEntryId);

            journalEntry.Date = model.Date;
            journalEntry.Memo = model.Memo;
            journalEntry.ReferenceNo = model.ReferenceNo;

            foreach (var line in model.AddJournalEntryLines)
            {
                if (journalEntry.JournalEntryLines.Any(l => l.AccountId == line.AccountId))
                {
                    var existingLine = journalEntry.JournalEntryLines.Where(l => l.AccountId == line.AccountId).FirstOrDefault();
                    existingLine.DrCr = line.DrCr;
                    existingLine.Amount = line.Amount;
                    existingLine.Memo = line.Memo;
                }
                else
                {
                    journalEntry.JournalEntryLines.Add(new JournalEntryLine()
                    {
                        AccountId = line.AccountId,
                        DrCr = line.DrCr,
                        Amount = line.Amount,
                        Memo = line.Memo
                    });
                }
            }

            _financialService.UpdateJournalEntry(journalEntry, model.Posted);

            return RedirectToAction("JournalEntries");
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

        [HttpPost, ActionName("EditJournalEntry")]
        [FormValueRequiredAttribute("DeleteJournalEntryLine")]
        public ActionResult UpdateJournalEntryLine(Models.ViewModels.Financials.AddJournalEntry model)
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
                VoucherType = model.JournalVoucherType
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

        public ActionResult MasterGeneralLedger(DateTime? from = default(DateTime?),
            DateTime? to = default(DateTime?),
            string accountCode = null,
            int? transactionNo = null)
        {
            var model = _financialService.MasterGeneralLedger(from, to, accountCode, transactionNo);
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
            //if (netincome < 0)
            //{
            //    var loss = model.Where(a => a.AccountCode == "30500").FirstOrDefault();
            //    loss.Amount = netincome;
            //}
            //else
            //{
            //    var profit = model.Where(a => a.AccountCode == "30400").FirstOrDefault();
            //    profit.Amount = netincome;
            //}

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
            model.CompanyId = glSetting.CompanyId;
            model.CompanyCode = _administrationService.GetDefaultCompany().CompanyCode;
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
            glSetting.CompanyId = model.CompanyId;
            glSetting.PayableAccountId = model.PayableAccountId;
            glSetting.PurchaseDiscountAccountId = model.PurchaseDiscountAccountId;
            glSetting.SalesDiscountAccountId = model.SalesDiscountAccountId;
            glSetting.ShippingChargeAccountId = model.ShippingChargeAccountId;
            glSetting.GoodsReceiptNoteClearingAccountId = model.GoodsReceiptNoteClearingAccountId;
            _financialService.UpdateGeneralLedgerSetting(glSetting);
            return View(model);
        }

        public ActionResult EditMasterGL(int id)
        {
            var gl = _financialService.GetGeneralLedgerHeader(id);

            if (gl.DocumentType == Core.Domain.DocumentTypes.JournalEntry)
                return RedirectToAction("EditJournalEntry", new { id = id, fromGL = true });

            return View();
        }

        public ActionResult TaxGroups()
        {
            var taxGroups = _financialService.GetTaxGroups();
            var model = new List<TaxGroupModel>();

            foreach(var group in taxGroups)
            {
                var groupTaxes = new List<TaxGroupTaxModel>();

                foreach (var groupTax in group.TaxGroupTax)
                {
                    groupTaxes.Add(new TaxGroupTaxModel()
                    {
                        Id = groupTax.Id,
                        TaxId = groupTax.TaxId,
                        TaxGroupId = groupTax.TaxGroupId
                    });
                }

                model.Add(new TaxGroupModel()
                {
                    Id = group.Id,
                    Description = group.Description,
                    TaxAppliedToShipping = group.TaxAppliedToShipping,
                    IsActive = group.IsActive,      
                    TaxGroupTaxModel = groupTaxes
                });
            }

            return View(model);
        }
    }
}
