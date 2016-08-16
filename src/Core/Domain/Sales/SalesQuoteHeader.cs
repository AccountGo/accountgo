//-----------------------------------------------------------------------
// <copyright file="SalesQuoteHeader.cs" company="AccountGo">
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
    [Table("SalesQuoteHeader")]
    public partial class SalesQuoteHeader : BaseEntity
    {
        public int CustomerId { get; set; }
        public int? PaymentTermId { get; set; }
        public string ReferenceNo { get; set; }
        public string No { get; set; }
        public SalesQuoteStatus? Status { get; set; }
        public DateTime Date { get; set; }
        public virtual Customer Customer { get; set; }
        public virtual ICollection<SalesQuoteLine> SalesQuoteLines { get; set; }

        public SalesQuoteHeader()
        {
            SalesQuoteLines = new HashSet<SalesQuoteLine>();
        }
    }
}
