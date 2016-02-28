//-----------------------------------------------------------------------
// <copyright file="Banks.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Core.Domain;

namespace Web.Models.ViewModels.Financials
{
    public partial class Banks
    {
        public Banks()
        {
            BankList = new HashSet<BankListLine>();
        }

        public ICollection<BankListLine> BankList { get; set; }
    }

    public partial class BankListLine
    {
        public BankTypes Type { get; set; }
        public string Name { get; set; }
        public int? AccountId { get; set; }
        public string BankName { get; set; }
        public string Number { get; set; }
        public string Address { get; set; }
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }
    }
}
