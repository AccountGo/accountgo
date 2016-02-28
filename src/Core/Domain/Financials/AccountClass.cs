//-----------------------------------------------------------------------
// <copyright file="AccountClass.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Financials;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Financials
{
    [Table("AccountClass")]
    public class AccountClass : BaseEntity
    {
        public AccountClass()
        {
            Accounts = new HashSet<Account>();
        }

        public string Name { get; set; }
        public string NormalBalance { get; set; }
        public virtual ICollection<Account> Accounts { get; set; }
    }
}
