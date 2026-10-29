//-----------------------------------------------------------------------
// <copyright file="SalesReceiptHeader.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Financials;
using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

#nullable enable

namespace Core.Domain.Sales
{
    [Table("SalesReceiptHeader")]
    public partial class SalesReceiptHeader : BaseEntity, ICompanyOwned
    {
        public SalesReceiptHeader()
        {
            SalesReceiptLines = new HashSet<SalesReceiptLine>();
            CustomerAllocations = new HashSet<CustomerAllocation>();
        }

        public int CompanyId { get; set; }

        public int CustomerId { get; set; }
        public int? GeneralLedgerHeaderId { get; set; }
        public int? AccountToDebitId { get; set; }
        public string? No { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public int? Status { get; set; }
        public string? PaymentMethod { get; set; }
        public string? ReferenceNo { get; set; }
        public string? Memo { get; set; }
        public virtual Customer? Customer { get; set; }
        public virtual GeneralLedgerHeader? GeneralLedgerHeader { get; set; }
        public virtual Account? AccountToDebit { get; set; }
        public virtual ICollection<SalesReceiptLine> SalesReceiptLines { get; set; }
        public virtual ICollection<CustomerAllocation> CustomerAllocations { get; set; }

        [NotMapped]
        public decimal AvailableAmountToAllocate { get { return GetAvailableAmountToAllocate(); } }
        private decimal GetAvailableAmountToAllocate()
        {
            return SalesReceiptLines.Sum(a => a.AmountPaid) - CustomerAllocations.Sum(a => a.Amount);
        }
    }
}
