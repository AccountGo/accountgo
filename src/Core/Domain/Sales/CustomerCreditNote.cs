// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.TaxSystem;

namespace Core.Domain.Sales
{
    [Table("CustomerCreditNote")]
    [PublicIdPrefix("cn")]
    public class CustomerCreditNote : BaseEntity, ICompanyOwned, IHasPublicId
    {
        public string PublicId { get; set; } = string.Empty;
        public CustomerCreditNote()
        {
            Lines = new HashSet<CustomerCreditNoteLine>();
        }

        public int CompanyId { get; set; }
        public int CustomerId { get; set; }
        public int? SourceSalesInvoiceHeaderId { get; set; }

        [MaxLength(64)]
        public string? No { get; set; }
        public DateTime Date { get; set; }

        [MaxLength(12)]
        public string CurrencyCode { get; set; } = "USD";
        public int CurrencyPrecision { get; set; } = 2;

        public CreditNoteKind Kind { get; set; } = CreditNoteKind.PriceOrServiceAdjustment;
        public bool ReturnGoodsWithoutRestock { get; set; }

        [MaxLength(500)]
        public string Reason { get; set; } = "";
        [MaxLength(128)]
        public string? SourceReference { get; set; }
        [MaxLength(500)]
        public string? EvidenceReference { get; set; }

        public string? SellerJson { get; set; }
        public string? BillToJson { get; set; }

        public int? CustomerCreditLiabilityAccountId { get; set; }
        public int? SalesReturnsAccountId { get; set; }
        public int? ReceivableAccountId { get; set; }

        public decimal Subtotal { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal AppliedAmount { get; set; }
        public decimal RefundedAmount { get; set; }

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

        public virtual Customer Customer { get; set; } = null!;
        public virtual SalesInvoiceHeader? SourceSalesInvoiceHeader { get; set; }
        public virtual GeneralLedgerHeader? GeneralLedgerHeader { get; set; }
        public virtual GeneralLedgerHeader? VoidGeneralLedgerHeader { get; set; }
        public virtual Account? CustomerCreditLiabilityAccount { get; set; }
        public virtual Account? SalesReturnsAccount { get; set; }
        public virtual Account? ReceivableAccount { get; set; }
        public virtual ICollection<CustomerCreditNoteLine> Lines { get; set; } = null!;

        [NotMapped]
        public decimal AvailableAmount => Math.Max(0m, TotalAmount - AppliedAmount - RefundedAmount);
    }

    [Table("CustomerCreditNoteLine")]
    [PublicIdPrefix("cnl")]
    public class CustomerCreditNoteLine : BaseEntity, ICompanyOwned, IHasPublicId
    {
        public string PublicId { get; set; } = string.Empty;
        public int CompanyId { get; set; }
        public int CustomerCreditNoteId { get; set; }
        public int? SourceSalesInvoiceLineId { get; set; }
        public int? ItemId { get; set; }
        public int? TaxId { get; set; }
        public int? TaxAccountId { get; set; }
        public decimal TaxRateSnapshot { get; set; }
        public bool TaxInclusiveSnapshot { get; set; }
        public bool IsShippingLine { get; set; }

        [MaxLength(500)]
        public string Description { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal Subtotal { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }

        public virtual CustomerCreditNote? CustomerCreditNote { get; set; }
        public virtual SalesInvoiceLine? SourceSalesInvoiceLine { get; set; }
        public virtual Item Item { get; set; } = null!;
        public virtual Tax? Tax { get; set; }
        public virtual Account? TaxAccount { get; set; }
    }

    [Table("CustomerReturnReceipt")]
    [PublicIdPrefix("crr")]
    public class CustomerReturnReceipt : BaseEntity, ICompanyOwned, IHasPublicId
    {
        public string PublicId { get; set; } = string.Empty;
        public CustomerReturnReceipt()
        {
            Lines = new HashSet<CustomerReturnReceiptLine>();
        }

        public int CompanyId { get; set; }
        public int CustomerId { get; set; }
        public int SalesInvoiceHeaderId { get; set; }
        public int? CustomerCreditNoteId { get; set; }

        [MaxLength(64)]
        public string? No { get; set; }
        public DateTime ReceiptDate { get; set; }
        [MaxLength(500)]
        public string? Notes { get; set; }

        public SettlementDocumentStatus Status { get; set; } = SettlementDocumentStatus.Draft;
        public int? GeneralLedgerHeaderId { get; set; }
        public int? VoidGeneralLedgerHeaderId { get; set; }

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

        public virtual Customer Customer { get; set; } = null!;
        public virtual SalesInvoiceHeader SalesInvoiceHeader { get; set; } = null!;
        public virtual CustomerCreditNote? CustomerCreditNote { get; set; }
        public virtual GeneralLedgerHeader? GeneralLedgerHeader { get; set; }
        public virtual GeneralLedgerHeader? VoidGeneralLedgerHeader { get; set; }
        public virtual ICollection<CustomerReturnReceiptLine> Lines { get; set; } = null!;
    }

    [Table("CustomerReturnReceiptLine")]
    [PublicIdPrefix("crrl")]
    public class CustomerReturnReceiptLine : BaseEntity, ICompanyOwned, IHasPublicId
    {
        public string PublicId { get; set; } = string.Empty;
        public int CompanyId { get; set; }
        public int CustomerReturnReceiptId { get; set; }
        public int SalesInvoiceLineId { get; set; }
        public int ItemId { get; set; }
        public decimal Quantity { get; set; }
        public ReturnCondition Condition { get; set; } = ReturnCondition.Saleable;
        public decimal OriginalUnitCost { get; set; }
        public decimal RecoverableUnitCost { get; set; }
        public decimal TotalRestoredCost { get; set; }
        public int? InventoryControlJournalId { get; set; }
        public int? InventoryAccountId { get; set; }
        public int? CogsAccountId { get; set; }

        public virtual CustomerReturnReceipt CustomerReturnReceipt { get; set; } = null!;
        public virtual SalesInvoiceLine SalesInvoiceLine { get; set; } = null!;
        public virtual Item Item { get; set; } = null!;
        public virtual InventoryControlJournal? InventoryControlJournal { get; set; }
        public virtual Account? InventoryAccount { get; set; }
        public virtual Account? CogsAccount { get; set; }
    }

    [Table("ReceivableWriteOff")]
    [PublicIdPrefix("wo")]
    public class ReceivableWriteOff : BaseEntity, ICompanyOwned, IHasPublicId
    {
        public string PublicId { get; set; } = string.Empty;
        public ReceivableWriteOff()
        {
            Recoveries = new HashSet<WriteOffRecovery>();
        }

        public int CompanyId { get; set; }
        public int CustomerId { get; set; }
        public int SalesInvoiceHeaderId { get; set; }

        [MaxLength(64)]
        public string? No { get; set; }
        public DateTime Date { get; set; }
        [MaxLength(12)]
        public string CurrencyCode { get; set; } = "USD";
        public decimal Amount { get; set; }
        public decimal AllowanceAllocatedAmount { get; set; }
        public decimal ImpairmentShortfallAmount { get; set; }
        public decimal RecoveredAmount { get; set; }

        public int? ReceivableAccountId { get; set; }
        public int? LossAllowanceAccountId { get; set; }
        public int? ImpairmentExpenseAccountId { get; set; }

        [MaxLength(500)]
        public string Reason { get; set; } = "";
        [MaxLength(500)]
        public string CollectionEvidence { get; set; } = "";

        public SettlementDocumentStatus Status { get; set; } = SettlementDocumentStatus.Draft;
        public int? GeneralLedgerHeaderId { get; set; }
        public int? VoidGeneralLedgerHeaderId { get; set; }

        [MaxLength(128)]
        public string? SubmittedBy { get; set; }
        public DateTime? SubmittedAtUtc { get; set; }
        [MaxLength(128)]
        public string? ReviewedBy { get; set; }
        public DateTime? ReviewedAtUtc { get; set; }
        public string? PolicySnapshotJson { get; set; }
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

        public virtual Customer Customer { get; set; } = null!;
        public virtual SalesInvoiceHeader SalesInvoiceHeader { get; set; } = null!;
        public virtual GeneralLedgerHeader? GeneralLedgerHeader { get; set; }
        public virtual GeneralLedgerHeader? VoidGeneralLedgerHeader { get; set; }
        public virtual ICollection<WriteOffRecovery> Recoveries { get; set; } = null!;
    }

    [Table("WriteOffRecovery")]
    [PublicIdPrefix("wor")]
    public class WriteOffRecovery : BaseEntity, ICompanyOwned, IHasPublicId
    {
        public string PublicId { get; set; } = string.Empty;
        public int CompanyId { get; set; }
        public int ReceivableWriteOffId { get; set; }

        [MaxLength(64)]
        public string? No { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public int BankId { get; set; }
        public int BankAccountId { get; set; }
        public int BadDebtRecoveryAccountId { get; set; }
        [MaxLength(128)]
        public string? ReferenceNo { get; set; }
        [MaxLength(500)]
        public string? EvidenceReference { get; set; }

        public SettlementDocumentStatus Status { get; set; } = SettlementDocumentStatus.Posted;
        public int? GeneralLedgerHeaderId { get; set; }
        public int? VoidGeneralLedgerHeaderId { get; set; }
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

        public virtual ReceivableWriteOff ReceivableWriteOff { get; set; } = null!;
        public virtual Bank Bank { get; set; } = null!;
        public virtual GeneralLedgerHeader? GeneralLedgerHeader { get; set; }
        public virtual GeneralLedgerHeader? VoidGeneralLedgerHeader { get; set; }
    }
}
