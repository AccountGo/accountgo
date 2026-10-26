//-----------------------------------------------------------------------
// <copyright file="Party.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
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
    public partial class Party : BaseEntity, ICompanyOwned
    {
        public Party()
        {
            Contacts = new HashSet<Contact>();
        }

        public int CompanyId { get; set; }

        public PartyTypes PartyType { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Website { get; set; }
        public string Phone { get; set; }
        public string Fax { get; set; }
        public bool IsActive { get; set; }
        public string CompanyStreet { get; set; }
        public string CompanyProvince { get; set; }
        public string CompanyPostalCode { get; set; }
        public string CompanyCountry { get; set; }
        public string ShippingStreet { get; set; }
        public string ShippingProvince { get; set; }
        public string ShippingPostalCode { get; set; }
        public string ShippingCountry { get; set; }

        public virtual ICollection<Contact> Contacts { get; set; }
    }
}
