//-----------------------------------------------------------------------
// <copyright file="VendorPayment.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Financials;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Purchases
{

    [Table("VendorPayment")]
    public partial class VendorPayment : BaseEntity, ICompanyOwned
    {        
        public VendorPayment()
        { }

        public int CompanyId { get; set; }

        public int VendorId { get; set; }
        public int? PurchaseInvoiceHeaderId { get; set; }
        public int? GeneralLedgerHeaderId { get; set; }
        public int? VoidGeneralLedgerHeaderId { get; set; }
        public string No { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        /// <summary>The cash or bank account the payment was made from.</summary>
        public int? AccountId { get; set; }
        public string PaymentMethod { get; set; }
        public string ReferenceNo { get; set; }
        public string Memo { get; set; }
        public string VoidedBy { get; set; }
        public DateTime? VoidedAt { get; set; }
        public string VoidReason { get; set; }

        public bool IsVoid => VoidedAt.HasValue;

        public virtual Account Account { get; set; }
        public virtual Vendor Vendor { get; set; }
        public virtual GeneralLedgerHeader GeneralLedgerHeader { get; set; }
        public virtual GeneralLedgerHeader VoidGeneralLedgerHeader { get; set; }
        public virtual PurchaseInvoiceHeader PurchaseInvoiceHeader { get; set; }
    }
}
