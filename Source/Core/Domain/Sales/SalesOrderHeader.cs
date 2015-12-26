//-----------------------------------------------------------------------
// <copyright file="SalesOrderHeader.cs" company="AccountGo">
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
    [Table("SalesOrderHeader")]
    public partial class SalesOrderHeader : BaseEntity
    {
        public SalesOrderHeader()
        {
            SalesOrderLines = new HashSet<SalesOrderLine>();
        }

        public int? CustomerId { get; set; }
        public int? PaymentTermId { get; set; }
        public string No { get; set; }
        public string ReferenceNo { get; set; }
        public DateTime Date { get; set; }

        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public string ModifiedBy { get; set; }
        public DateTime ModifiedOn { get; set; }

        public virtual Customer Customer { get; set; }
        public virtual PaymentTerm PaymentTerm { get; set; }

        public virtual ICollection<SalesOrderLine> SalesOrderLines { get; set; }
    }
}
