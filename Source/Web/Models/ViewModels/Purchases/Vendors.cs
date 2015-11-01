//-----------------------------------------------------------------------
// <copyright file="Vendors.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:50:13 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;

namespace Web.Models.ViewModels.Purchases
{
    public class Vendors
    {
        public Vendors()
        {
            VendorsList = new HashSet<VendorsListLine>();
        }

        public virtual ICollection<VendorsListLine> VendorsList { get; set; }
    }

    public class VendorsListLine
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Balance { get; set; }
    }

    public class Vendor
    {
        public Vendor()
        {
            Accounts = new HashSet<SelectListItem>();
        }

        public int? Id { get; set; }
        [Required]
        public string VendorName { get; set; }
        public int? TaxGroupId { get; set; }
        public int? AccountsPayableAccountId { get; set; }
        public int? PurchaseAccountId { get; set; }
        public int? PurchaseDiscountAccountId { get; set; }
        public virtual string FirstName { get; set; }
        public virtual string LastName { get; set; }
        public virtual string MiddleName { get; set; }
        public virtual string Email { get; set; }
        public virtual string Website { get; set; }
        public virtual string Phone { get; set; }
        public virtual string Fax { get; set; }
        public virtual bool IsActive { get; set; }

        public virtual ICollection<SelectListItem> Accounts { get; set; }
    }

    public class AddVendor
    {
        public AddVendor()
        {
            Accounts = new HashSet<SelectListItem>();
        }
        public string VendorName { get; set; }
        public int? TaxGroupId { get; set; }
        public int? AccountsPayableAccountId { get; set; }
        public int? PurchaseAccountId { get; set; }
        public int? PurchaseDiscountAccountId { get; set; }

        public virtual ICollection<SelectListItem> Accounts { get; set; }
    }
}
