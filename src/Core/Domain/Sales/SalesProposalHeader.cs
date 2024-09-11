//-----------------------------------------------------------------------
// <copyright file="SalesProposalHeader.cs" company="GoodDeedBooks">
// Copyright (c) GoodDeedBooks. All rights reserved.
// <author>Yeongdong Choi</author>
// <date>4/09/2024 5:31:40 PM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Sales
{
    [Table("SalesProposalHeader")]
    public class SalesProposalHeader : BaseEntity
    {
        // Properties
        public string No { get; set; }
        public string Description { get; set; }
        public DateTime DeliveryDate { get; set; }
        public SalesProposalStatus? Status { get; set; }
        public string ReferenceNo { get; set; }

        // Foreign keys
        public int CustomerId { get; set; }
        public int? PaymentTermId { get; set; }


        // Navigation properties
        public virtual Customer Customer { get; set; }
        public virtual PaymentTerm PaymentTerm { get; set; }
        public virtual ICollection<SalesProposalLine> SalesProposalLines { get; set; }



        public SalesProposalHeader()
        {
            SalesProposalLines = new HashSet<SalesProposalLine>();
        }
    }
}