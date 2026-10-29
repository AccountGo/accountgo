//-----------------------------------------------------------------------
// <copyright file="SalesOrderHeader.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
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
    public partial class SalesOrderHeader : BaseEntity, ICompanyOwned
    {
        public SalesOrderHeader()
        {
            SalesOrderLines = new HashSet<SalesOrderLine>();
        }

        public int CompanyId { get; set; }

        public int? CustomerId { get; set; }
        public int? PaymentTermId { get; set; }
        public string No { get; set; }
        public string ReferenceNo { get; set; }
        public SalesOrderStatus? Status { get; set; }
        public DateTime Date { get; set; }
        public virtual Customer Customer { get; set; }
        public virtual PaymentTerm PaymentTerm { get; set; }

        public virtual ICollection<SalesOrderLine> SalesOrderLines { get; set; }
    }
}
