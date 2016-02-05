//-----------------------------------------------------------------------
// <copyright file="SalesReceiptLine.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Financials;
using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Sales
{
    [Table("SalesReceiptLine")]
    public partial class SalesReceiptLine : BaseEntity
    {
        public SalesReceiptLine()
        {            
        }

        public int SalesReceiptHeaderId { get; set; }
        public int? SalesInvoiceLineId { get; set; }
        public int? ItemId { get; set; }
        public int? AccountToCreditId { get; set; }
        public int? MeasurementId { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? Discount { get; set; }
        public decimal? Amount { get; set; }
        public decimal AmountPaid { get; set; }
        public virtual SalesReceiptHeader SalesReceiptHeader { get; set; }
        public virtual SalesInvoiceLine SalesInvoiceLine { get; set; }
        public virtual Account AccountToCredit { get; set; }
    }
}
