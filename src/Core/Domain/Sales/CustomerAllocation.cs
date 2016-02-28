//-----------------------------------------------------------------------
// <copyright file="CustomerAllocation.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Sales
{
    [Table("CustomerAllocation")]
    public partial class CustomerAllocation : BaseEntity
    {
        public CustomerAllocation()
        {
        }

        public int CustomerId { get; set; }
        public int SalesInvoiceHeaderId { get; set; }
        public int SalesReceiptHeaderId { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }

        public virtual Customer Customer { get; set; }
        public virtual SalesInvoiceHeader SalesInvoiceHeader { get; set; }
        public virtual SalesReceiptHeader SalesReceiptHeader { get; set; }
    }
}
