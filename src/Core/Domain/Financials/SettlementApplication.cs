// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Core.Domain.Purchases;
using Core.Domain.Sales;

namespace Core.Domain.Financials
{
    [Table("SettlementApplication")]
    public class SettlementApplication : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }

        public SettlementSourceType SourceType { get; set; }
        public int? CustomerCreditNoteId { get; set; }
        public int? SupplierCreditNoteId { get; set; }
        public int? SupplierAdvanceId { get; set; }

        public SettlementDestinationType DestinationType { get; set; }
        public int? SalesInvoiceHeaderId { get; set; }
        public int? PurchaseInvoiceHeaderId { get; set; }

        [MaxLength(12)]
        public string CurrencyCode { get; set; } = "USD";
        public decimal Amount { get; set; }
        public DateTime EffectiveDate { get; set; }

        public int SourceAccountId { get; set; }
        public int DestinationAccountId { get; set; }
        public int GeneralLedgerHeaderId { get; set; }

        [MaxLength(128)]
        public string CreatedBy { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; }
        public bool IsReversed { get; set; }

        [ConcurrencyCheck]
        public long Version { get; set; }

        public virtual CustomerCreditNote? CustomerCreditNote { get; set; }
        public virtual SupplierCreditNote? SupplierCreditNote { get; set; }
        public virtual SupplierAdvance? SupplierAdvance { get; set; }
        public virtual SalesInvoiceHeader? SalesInvoiceHeader { get; set; }
        public virtual PurchaseInvoiceHeader? PurchaseInvoiceHeader { get; set; }
        public virtual GeneralLedgerHeader? GeneralLedgerHeader { get; set; }
        public virtual SettlementApplicationReversal? Reversal { get; set; }
    }

    [Table("SettlementApplicationReversal")]
    public class SettlementApplicationReversal : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public int SettlementApplicationId { get; set; }
        public DateTime ReversalDate { get; set; }

        [MaxLength(500)]
        public string Reason { get; set; } = "";
        [MaxLength(128)]
        public string ReversedBy { get; set; } = "";
        public DateTime ReversedAtUtc { get; set; }
        public int GeneralLedgerHeaderId { get; set; }

        public virtual SettlementApplication? SettlementApplication { get; set; }
        public virtual GeneralLedgerHeader? GeneralLedgerHeader { get; set; }
    }

    [Table("PartyRefund")]
    public class PartyRefund : BaseEntity, ICompanyOwned
    {
        public PartyRefund()
        {
            Allocations = new HashSet<PartyRefundAllocation>();
        }

        public int CompanyId { get; set; }
        public PartyRefundDirection Direction { get; set; }
        public int? CustomerId { get; set; }
        public int? VendorId { get; set; }

        [MaxLength(64)]
        public string? No { get; set; }
        public DateTime Date { get; set; }
        [MaxLength(12)]
        public string CurrencyCode { get; set; } = "USD";
        public int CurrencyPrecision { get; set; } = 2;
        public decimal Amount { get; set; }

        public int BankId { get; set; }
        public int BankAccountId { get; set; }
        [MaxLength(64)]
        public string PaymentMethod { get; set; } = "Bank Transfer";
        [MaxLength(128)]
        public string? ExternalReference { get; set; }
        [MaxLength(500)]
        public string? EvidenceReference { get; set; }

        public SettlementDocumentStatus Status { get; set; } = SettlementDocumentStatus.Draft;
        public int? GeneralLedgerHeaderId { get; set; }
        public int? VoidGeneralLedgerHeaderId { get; set; }

        [MaxLength(128)]
        public string? SubmittedBy { get; set; }
        public DateTime? SubmittedAtUtc { get; set; }
        [MaxLength(128)]
        public string? ReviewedBy { get; set; }
        public DateTime? ReviewedAtUtc { get; set; }
        [MaxLength(128)]
        public string? PostedBy { get; set; }
        public DateTime? PostedAtUtc { get; set; }
        [MaxLength(128)]
        public string? ReversedBy { get; set; }
        public DateTime? ReversedAtUtc { get; set; }
        [MaxLength(500)]
        public string? ReversalReason { get; set; }

        [ConcurrencyCheck]
        public long Version { get; set; }

        public virtual Customer? Customer { get; set; }
        public virtual Vendor? Vendor { get; set; }
        public virtual Bank? Bank { get; set; }
        public virtual GeneralLedgerHeader? GeneralLedgerHeader { get; set; }
        public virtual GeneralLedgerHeader? VoidGeneralLedgerHeader { get; set; }
        public virtual ICollection<PartyRefundAllocation> Allocations { get; set; }
    }

    [Table("PartyRefundAllocation")]
    public class PartyRefundAllocation : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public int PartyRefundId { get; set; }
        public SettlementSourceType SourceType { get; set; }
        public int? CustomerCreditNoteId { get; set; }
        public int? SupplierCreditNoteId { get; set; }
        public int? SupplierAdvanceId { get; set; }
        public int SourceAccountId { get; set; }
        public decimal Amount { get; set; }

        public virtual PartyRefund? PartyRefund { get; set; }
        public virtual CustomerCreditNote? CustomerCreditNote { get; set; }
        public virtual SupplierCreditNote? SupplierCreditNote { get; set; }
        public virtual SupplierAdvance? SupplierAdvance { get; set; }
    }

    [Table("SettlementCommandReceipt")]
    public class SettlementCommandReceipt : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        [MaxLength(128)]
        public string OperationKey { get; set; } = "";
        [MaxLength(128)]
        public string IdempotencyKey { get; set; } = "";
        [MaxLength(64)]
        public string PayloadHash { get; set; } = "";
        [MaxLength(64)]
        public string ResultEntityType { get; set; } = "";
        public int ResultEntityId { get; set; }
        public string ResultJson { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; }
    }
}
