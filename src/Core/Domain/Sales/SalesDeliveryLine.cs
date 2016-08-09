//-----------------------------------------------------------------------
// <copyright file="SalesDeliveryLine.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Items;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Sales
{
    [Table("SalesDeliveryLine")]
    public partial class SalesDeliveryLine : BaseEntity
    {
        public int SalesDeliveryHeaderId { get; set; }
        public int? SalesInvoiceLineId { get; set; }
        public int? ItemId { get; set; }
        public int? MeasurementId { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Discount { get; set; }

        public virtual SalesDeliveryHeader SalesDeliveryHeader { get; set; }
        public virtual SalesInvoiceLine SalesInvoiceLine { get; set; }
        public virtual Item Item { get; set; }
        public virtual Measurement Measurement { get; set; }
    }
}
