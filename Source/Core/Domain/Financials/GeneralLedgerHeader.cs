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

        public virtual DateTime Date { get; set; }
        public virtual DocumentTypes DocumentType { get; set; }
        public virtual string Description { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public string ModifiedBy { get; set; }
        public DateTime ModifiedOn { get; set; }

        public virtual ICollection<GeneralLedgerLine> GeneralLedgerLines { get; set; }
        public virtual ICollection<PurchaseReceiptHeader> PurchaseOrderReceipts { get; set; }

        public ICollection<GeneralLedgerLine> Assets()
        {
            var assets = GeneralLedgerLines.Where(a => a.Account.AccountClassId == (int)AccountClasses.Assets).ToList();

            return assets;
        }

        public ICollection<GeneralLedgerLine> Liabilities()
        {
            var liablities = GeneralLedgerLines.Where(a => a.Account.AccountClassId == (int)AccountClasses.Liabilities).ToList();

            return liablities;
        }

        public ICollection<GeneralLedgerLine> Equities()
        {
            var equities = GeneralLedgerLines.Where(a => a.Account.AccountClassId == (int)AccountClasses.Equity).ToList();

            return equities;
        }

        public ICollection<GeneralLedgerLine> Revenues()
        {
            var revenues = GeneralLedgerLines.Where(a => a.Account.AccountClassId == (int)AccountClasses.Revenue).ToList();

            return revenues;
        }

        public ICollection<GeneralLedgerLine> Expenses()
        {
            var expenses = GeneralLedgerLines.Where(a => a.Account.AccountClassId == (int)AccountClasses.Expense).ToList();

            return expenses;
        }

        /// <summary>
        /// Assets = Liabilities + Equities
        /// </summary>
        /// <returns></returns>
        public bool ValidateAccountingEquation()
        {
            bool isEqual = true;

            isEqual = Assets().Sum(a => a.Amount) == Liabilities().Sum(l => l.Amount) + Equities().Sum(e => e.Amount);

            return isEqual;
        }

        public bool ValidateLiabilitiesEqualsExpenses()
        {
            bool isEqual = true;

            isEqual = Liabilities().Sum(l => l.Amount) >= Expenses().Sum(e => e.Amount);

            return isEqual;
        }

        public bool ValidateAssetsEqualsRevenues()
        {
            bool isEqual = true;

            isEqual = Assets().Sum(a => a.Amount) >= Revenues().Sum(r => r.Amount);

            return isEqual;
        }

        public bool ValidateAssetsEqualsEquities()
        {
            bool isEqual = true;

            isEqual = Assets().Sum(a => a.Amount) >= Equities().Sum(e => e.Amount);

            return isEqual;
        }
    }
}
