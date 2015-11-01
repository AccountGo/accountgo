//-----------------------------------------------------------------------
// <copyright file="JournalEntries.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:50:13 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace Web.Models.ViewModels.Financials
{
    public class JournalEntries
    {
        public JournalEntries()
        {
            JournalEntriesListLines = new HashSet<JournalEntriesListLine>();
        }

        public DateTime? From { get; set; }
        public DateTime? To { get; set; }

        public ICollection<JournalEntriesListLine> JournalEntriesListLines { get; set; }
    }

    public class JournalEntriesListLine
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; }
        public string AccountName { get; set; }
        public string DrCr { get; set; }
        public decimal Amount { get; set; }
    }

    public class AddJournalEntry
    {
        public AddJournalEntry()
        {
            Accounts = new HashSet<SelectListItem>();
            AddJournalEntryLines = new List<AddJournalEntryLine>();
            Date = DateTime.Now;
        }

        public DateTime Date { get; set; }
        public string ReferenceNo { get; set; }
        public string Memo { get; set; }

        public ICollection<SelectListItem> Accounts { get; set; }
        public IList<AddJournalEntryLine> AddJournalEntryLines { get; set; }

        #region Fields for New Journal Entry
        public int AccountId { get; set; }
        public TransactionTypes DrCr { get; set; }
        public decimal Amount { get; set; }
        public string MemoLine { get; set; }
        #endregion
    }

    public class AddJournalEntryLine
    {
        public string RowId { get; set; }
        public int AccountId { get; set; }
        public string AccountName { get; set; }
        public TransactionTypes DrCr { get; set; }
        public decimal Amount { get; set; }
        public string Memo { get; set; }
    }
}
