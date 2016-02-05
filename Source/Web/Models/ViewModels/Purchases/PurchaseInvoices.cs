//-----------------------------------------------------------------------
// <copyright file="PurchaseInvoices.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace Web.Models.ViewModels.Purchases
{
    public class PurchaseInvoices
    {
        public PurchaseInvoices()
        {
            PurchaseInvoiceListLines = new HashSet<PurchaseInvoiceListLine>();
        }

        public ICollection<PurchaseInvoiceListLine> PurchaseInvoiceListLines { get; set; }
    }

    public class PurchaseInvoiceListLine
    {
        public int Id { get; set; }
        public string No { get; set; }
        public DateTime Date { get; set; }
        public string Vendor { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal TotalTax { get; set; }
        public string Status { get; set; }
        public bool IsPaid { get; set; }
    }

    public class MakePayment
    {
        public MakePayment()
        {
        }
        public int InvoiceId { get; set; }
        public int AccountId { get; set; }
        public string InvoiceNo { get; set; }
        public string Vendor { get; set; }
        public decimal Amount { get; set; }
        public decimal AmountToPay { get; set; }
    }
}
