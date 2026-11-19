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

namespace Core.Domain.Purchases
{
    [Table("SupplierCreditNote")]
    public class SupplierCreditNote : BaseEntity, ICompanyOwned
    {
        public SupplierCreditNote()
        {
            Lines = new HashSet<SupplierCreditNoteLine>();
        }

        public int CompanyId { get; set; }
        public int VendorId { get; set; }
        public int? SourcePurchaseInvoiceHeaderId { get; set; }

        [MaxLength(64)]
        public string? No { get; set; }
        [MaxLength(128)]
        public string VendorCreditNo { get; set; } = "";
        public DateTime Date { get; set; }
        public DateTime EffectiveDate { get; set; }

        [MaxLength(12)]
        public string CurrencyCode { get; set; } = "USD";
        public int CurrencyPrecision { get; set; } = 2;

        public CreditNoteKind Kind { get; set; } = CreditNoteKind.PriceOrServiceAdjustment;
        public bool IsStandalone { get; set; }

        [MaxLength(500)]
        public string Reason { get; set; } = "";
        [MaxLength(500)]
        public string? EvidenceReference { get; set; }

        public string? BillFromJson { get; set; }

        public int? SupplierCreditAssetAccountId { get; set; }
        public int? PayableAccountId { get; set; }
        public int? ReturnClearingAccountId { get; set; }
        public int? ValuationDifferenceAccountId { get; set; }

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

        public virtual Vendor Vendor { get; set; } = null!;
        public virtual PurchaseInvoiceHeader? SourcePurchaseInvoiceHeader { get; set; }
        public virtual GeneralLedgerHeader? GeneralLedgerHeader { get; set; }
        public virtual GeneralLedgerHeader? VoidGeneralLedgerHeader { get; set; }
        public virtual Account? SupplierCreditAssetAccount { get; set; }
        public virtual Account? PayableAccount { get; set; }
        public virtual Account? ReturnClearingAccount { get; set; }
        public virtual Account? ValuationDifferenceAccount { get; set; }
        public virtual ICollection<SupplierCreditNoteLine> Lines { get; set; } = null!;

        [NotMapped]
        public decimal AvailableAmount => Math.Max(0m, TotalAmount - AppliedAmount - RefundedAmount);
    }

    [Table("SupplierCreditNoteLine")]
    public class SupplierCreditNoteLine : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public int SupplierCreditNoteId { get; set; }
        public int? SourcePurchaseInvoiceLineId { get; set; }
        public int? MatchedReturnDispatchId { get; set; }
        public int? ItemId { get; set; }
        public int? ExpenseOrAssetAccountId { get; set; }
        public int? TaxId { get; set; }
        public int? TaxAccountId { get; set; }
        public decimal TaxRateSnapshot { get; set; }
        public bool TaxRecoverableSnapshot { get; set; } = true;

        [MaxLength(500)]
        public string Description { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal UnitCost { get; set; }
        public decimal Subtotal { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }

        public virtual SupplierCreditNote? SupplierCreditNote { get; set; }
        public virtual PurchaseInvoiceLine? SourcePurchaseInvoiceLine { get; set; }
        public virtual SupplierReturnDispatch? MatchedReturnDispatch { get; set; }
        public virtual Item Item { get; set; } = null!;
        public virtual Account? ExpenseOrAssetAccount { get; set; }
        public virtual Tax? Tax { get; set; }
        public virtual Account? TaxAccount { get; set; }
    }

    [Table("SupplierAdvance")]
    public class SupplierAdvance : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public int VendorId { get; set; }
        public int? PurchaseOrderHeaderId { get; set; }

        [MaxLength(64)]
        public string? No { get; set; }
        public DateTime Date { get; set; }
        [MaxLength(12)]
        public string CurrencyCode { get; set; } = "USD";
        public int CurrencyPrecision { get; set; } = 2;

        public decimal Amount { get; set; }
        public decimal AppliedAmount { get; set; }
        public decimal RefundedAmount { get; set; }

        public int BankId { get; set; }
        public int BankAccountId { get; set; }
        public int SupplierAdvanceAccountId { get; set; }

        [MaxLength(500)]
        public string Purpose { get; set; } = "";
        public bool IsRefundable { get; set; } = true;
        [MaxLength(128)]
        public string? ReferenceNo { get; set; }
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

        public virtual Vendor Vendor { get; set; } = null!;
        public virtual PurchaseOrderHeader? PurchaseOrderHeader { get; set; }
        public virtual Bank Bank { get; set; } = null!;
        public virtual GeneralLedgerHeader? GeneralLedgerHeader { get; set; }
        public virtual GeneralLedgerHeader? VoidGeneralLedgerHeader { get; set; }

        [NotMapped]
        public decimal AvailableAmount => Math.Max(0m, Amount - AppliedAmount - RefundedAmount);
    }

    [Table("SupplierReturnDispatch")]
    public class SupplierReturnDispatch : BaseEntity, ICompanyOwned
    {
        public SupplierReturnDispatch()
        {
            Lines = new HashSet<SupplierReturnDispatchLine>();
        }

        public int CompanyId { get; set; }
        public int VendorId { get; set; }
        public int? PurchaseReceiptHeaderId { get; set; }
        public int? PurchaseInvoiceHeaderId { get; set; }
        public int? SupplierCreditNoteId { get; set; }

        [MaxLength(64)]
        public string? No { get; set; }
        public DateTime DispatchDate { get; set; }
        public DateTime? ControlTransferDate { get; set; }
        public bool HasControlTransferred { get; set; }

        [MaxLength(500)]
        public string Reason { get; set; } = "";
        [MaxLength(500)]
        public string? EvidenceReference { get; set; }

        public int? ReturnClearingAccountId { get; set; }

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

        public virtual Vendor Vendor { get; set; } = null!;
        public virtual PurchaseReceiptHeader? PurchaseReceiptHeader { get; set; }
        public virtual PurchaseInvoiceHeader? PurchaseInvoiceHeader { get; set; }
        public virtual SupplierCreditNote? SupplierCreditNote { get; set; }
        public virtual Account? ReturnClearingAccount { get; set; }
        public virtual GeneralLedgerHeader? GeneralLedgerHeader { get; set; }
        public virtual GeneralLedgerHeader? VoidGeneralLedgerHeader { get; set; }
        public virtual ICollection<SupplierReturnDispatchLine> Lines { get; set; } = null!;
    }

    [Table("SupplierReturnDispatchLine")]
    public class SupplierReturnDispatchLine : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public int SupplierReturnDispatchId { get; set; }
        public int? PurchaseReceiptLineId { get; set; }
        public int? PurchaseInvoiceLineId { get; set; }
        public int ItemId { get; set; }
        public decimal Quantity { get; set; }
        public decimal CarryingUnitCost { get; set; }
        public decimal TotalCarryingCost { get; set; }
        public int? InventoryControlJournalId { get; set; }
        public int? InventoryAccountId { get; set; }

        public virtual SupplierReturnDispatch SupplierReturnDispatch { get; set; } = null!;
        public virtual PurchaseReceiptLine? PurchaseReceiptLine { get; set; }
        public virtual PurchaseInvoiceLine? PurchaseInvoiceLine { get; set; }
        public virtual Item Item { get; set; } = null!;
        public virtual InventoryControlJournal? InventoryControlJournal { get; set; }
        public virtual Account? InventoryAccount { get; set; }
    }
}
