//-----------------------------------------------------------------------
// <copyright file="CustomerViewModel.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:50:13 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Web.Models.ViewModels.Sales
{
    public class CustomerViewModel
    {
        public CustomerViewModel()
        {
            CustomerAllocations = new HashSet<CustomerAllocation>();
            CustomerInvoices = new HashSet<CustomerSalesInvoice>();
        }

        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Balance { get; set; }

        public virtual ICollection<CustomerAllocation> CustomerAllocations { get; set; }
        public virtual ICollection<CustomerSalesInvoice> CustomerInvoices { get; set; }
    }
}
