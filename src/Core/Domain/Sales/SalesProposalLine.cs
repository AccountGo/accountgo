//-----------------------------------------------------------------------
// <copyright file="SalesProposalLine.cs" company="GoodDeedBooks">
// Copyright (c) GoodDeedBooks. All rights reserved.
// <author>Yeongdong Choi</author>
// <date>4/09/2024 5:31:40 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Items;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Sales
{
    [Table("SalesProposalLine")]
    public class SalesProposalLine : BaseEntity
    {
        public int SalesProposalHeaderId { get; set; }
        public int ItemId { get; set; }
        public int MeasurementId { get; set; }


        public decimal Quantity { get; set; }
        public decimal AmountExcludingTaxes { get; set; }
        public decimal PricePerUnit { get; set; }
        public decimal Discount { get; set; }


        public SalesProposalHeader SalesProposalHeader { get; set; }


        public virtual Item Item { get; set; }
        public virtual Measurement Measurement { get; set; }
    }
}
