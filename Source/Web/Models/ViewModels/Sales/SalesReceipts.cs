//-----------------------------------------------------------------------
// <copyright file="SalesReceipts.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Sales;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Web.Models.ViewModels.Sales
{
    public class SalesReceipts
    {
        public SalesReceipts()
        {
            SalesReceiptListLines = new HashSet<SalesReceiptListLine>();
        }

        public virtual ICollection<SalesReceiptListLine> SalesReceiptListLines { get; set; }
    }

    public class SalesReceiptListLine
    {
        public string No { get; set; }
        public string InvoiceNo { get; set; }
        public string CustomerName { get; set; }
        public int CustomerId { get; set; }
        public DateTime Date { get; set; }        
        public decimal? Amount { get; set; }
        public decimal? AmountPaid { get; set; }
    }

    public class AddSalesReceipt
    {
        public AddSalesReceipt()
        {
            AddSalesReceiptLines = new List<AddSalesReceiptLine>();
            CustomerOutstandingSalesInvoices = new List<SalesInvoiceHeader>();
            Date = DateTime.Now;
        }

        [UIHint("Into Bank Account")]
        public int? AccountToDebitId { get; set; }
        public int? AccountToCreditId { get; set; }
        public int? SalesInvoiceId { get; set; }
        public string SalesInvoiceNo { get; set; }
        public int? CustomerId { get; set; }
        public DateTime InvoiceDate { get; set; }
        public DateTime Date { get; set; }
        public decimal PaymentAmount { get; set; }

        public IList<AddSalesReceiptLine> AddSalesReceiptLines { get; set; }
        public IList<SalesInvoiceHeader> CustomerOutstandingSalesInvoices { get; set; }

        #region Fields to add new receipt
        public int? ItemId { get; set; }
        public int? MeasurementId { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? Discount { get; set; }
        public decimal? Amount { get; set; }
        public decimal AmountToPay { get; set; }
        public string AccountCode { get; set; }
        #endregion
    }

    public class AddSalesReceiptLine
    {
        public string RowId { get; set; }
        public int? SalesInvoiceLineId { get; set; }
        public int? ItemId { get; set; }
        public int? AccountToCreditId { get; set; }
        public int? MeasurementId { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? Discount { get; set; }
        public decimal? Amount { get; set; }
        public decimal AmountToPay { get; set; }
    }
}
