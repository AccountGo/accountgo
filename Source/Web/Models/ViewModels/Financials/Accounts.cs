//-----------------------------------------------------------------------
// <copyright file="Accounts.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Web.Models.ViewModels.Financials
{
    public class Accounts
    {
        public Accounts()
        {
            AccountsListLines = new HashSet<AccountsListLine>();
        }

        public ICollection<AccountsListLine> AccountsListLines { get; set; }
    }

    public class AccountsListLine
    {
        public int Id { get; set; }
        public string AccountCode { get; set; }
        public string AccountName { get; set; }
        public decimal Balance { get; set; }
    }
}
