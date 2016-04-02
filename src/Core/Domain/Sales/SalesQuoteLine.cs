//-----------------------------------------------------------------------
// <copyright file="SalesQuoteLine.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Items;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Sales
{
    [Table("SalesQuoteLine")]
    public partial class SalesQuoteLine : BaseEntity
    {
        public int SalesQuoteHeaderId { get; set; }
        public int ItemId { get; set; }
        public int MeasurementId { get; set; }
        public decimal Quantity { get; set; }
        public decimal Discount { get; set; }
        public decimal Amount { get; set; }
        public SalesQuoteHeader SalesQuoteHeader { get; set; }
        public virtual Item Item { get; set; }
        public virtual Measurement Measurement { get; set; }
    }
}
