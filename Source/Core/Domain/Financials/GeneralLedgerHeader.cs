//-----------------------------------------------------------------------
// <copyright file="GeneralLedgerHeader.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Purchases;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Core.Domain.Financials
{
    [Table("GeneralLedgerHeader")]
    public partial class GeneralLedgerHeader : BaseEntity
    {
        public GeneralLedgerHeader()
        {
            GeneralLedgerLines = new HashSet<GeneralLedgerLine>();
            PurchaseOrderReceipts = new HashSet<PurchaseReceiptHeader>();
        }

        public DateTime Date { get; set; }
        public DocumentTypes DocumentType { get; set; }
        public string Description { get; set; }

        public virtual ICollection<GeneralLedgerLine> GeneralLedgerLines { get; set; }
        public virtual ICollection<PurchaseReceiptHeader> PurchaseOrderReceipts { get; set; }

        public ICollection<GeneralLedgerLine> Assets()
        {
            var assets = GeneralLedgerLines.ToList().Where(a => a.Account.AccountClassId == (int)AccountClasses.Assets);

            return assets.ToList();
        }

        public ICollection<GeneralLedgerLine> Liabilities()
        {
            var liablities = GeneralLedgerLines.ToList().Where(a => a.Account.AccountClassId == (int)AccountClasses.Liabilities);

            return liablities.ToList();
        }

        public ICollection<GeneralLedgerLine> Equities()
        {
            var equities = GeneralLedgerLines.ToList().Where(a => a.Account.AccountClassId == (int)AccountClasses.Equity);

            return equities.ToList();
        }

        public ICollection<GeneralLedgerLine> Revenues()
        {
            var revenues = GeneralLedgerLines.ToList().Where(a => a.Account.AccountClassId == (int)AccountClasses.Revenue);

            return revenues.ToList();
        }

        public ICollection<GeneralLedgerLine> Expenses()
        {
            var expenses = GeneralLedgerLines.ToList().Where(a => a.Account.AccountClassId == (int)AccountClasses.Expense);

            return expenses.ToList();
        }

        public bool HaveAtLeastTwoAccountClass()
        {
            var grouped = this.GeneralLedgerLines.GroupBy(a => a.Account.AccountClassId);

            if (grouped.Count() > 1)
                return true;
            else
                return false;
        }

        /// <summary>
        /// Assets = Liabilities + Equities
        /// </summary>
        /// <returns></returns>
        public bool ValidateAccountingEquation()
        {
            bool isEqual = true;

            var assetsAmount = Assets() != null ? Assets().Sum(a => a.Amount) : 0;
            var liabilitiesAmount = Liabilities() != null ? Liabilities().Sum(a => a.Amount) : 0;
            var equitiesAmount = Equities() != null ? Equities().Sum(a => a.Amount) : 0;

            isEqual = assetsAmount == liabilitiesAmount + equitiesAmount;

            return isEqual;
        }

        public bool ValidateLiabilitiesEqualsExpenses()
        {
            bool isEqual = true;

            var liabilitiesAmount = Liabilities() != null ? Liabilities().Sum(a => a.Amount) : 0;
            var expensesAmount = Expenses() != null ? Expenses().Sum(a => a.Amount) : 0;

            isEqual = liabilitiesAmount >= expensesAmount;

            return isEqual;
        }

        public bool ValidateAssetsEqualsRevenues()
        {
            bool isEqual = true;

            var assetsAmount = Assets() != null ? Assets().Sum(a => a.Amount) : 0;
            var revenuesAmount = Revenues() != null ? Revenues().Sum(a => a.Amount) : 0;

            isEqual = assetsAmount >= revenuesAmount;

            return isEqual;
        }

        public bool ValidateAssetsEqualsEquities()
        {
            bool isEqual = true;
            
            var assetsAmount = Assets() != null ? Assets().Sum(a => a.Amount) : 0;
            var equitiesAmount = Equities() != null ? Equities().Sum(a => a.Amount) : 0;

            isEqual = assetsAmount >= equitiesAmount;

            return isEqual;
        }
    }
}
