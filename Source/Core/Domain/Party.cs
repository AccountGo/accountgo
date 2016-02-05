//-----------------------------------------------------------------------
// <copyright file="Party.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    [Table("Party")]
    public partial class Party : BaseEntity
    {
        public Party()
        {
            Contacts = new HashSet<Contact>();
        }

        public PartyTypes PartyType { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Website { get; set; }
        public string Phone { get; set; }
        public string Fax { get; set; }
        public bool IsActive { get; set; }

        public virtual ICollection<Contact> Contacts { get; set; }
    }
}
