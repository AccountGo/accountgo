// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Core.Domain.Financials;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace Api.Data
{
    public partial class ApiDbContext
    {
        private static void ConfigureCreditsReturnsRefundsModel(ModelBuilder builder)
        {
            builder.Entity<GeneralLedgerSetting>(entity =>
            {
                entity.HasOne(e => e.CustomerCreditLiabilityAccount).WithMany().HasForeignKey(e => e.CustomerCreditLiabilityAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.SalesReturnsAllowancesAccount).WithMany().HasForeignKey(e => e.SalesReturnsAllowancesAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.SupplierCreditAssetAccount).WithMany().HasForeignKey(e => e.SupplierCreditAssetAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.SupplierAdvanceAccount).WithMany().HasForeignKey(e => e.SupplierAdvanceAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.SupplierReturnClearingAccount).WithMany().HasForeignKey(e => e.SupplierReturnClearingAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.ReturnValuationDifferenceAccount).WithMany().HasForeignKey(e => e.ReturnValuationDifferenceAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.ReceivableLossAllowanceAccount).WithMany().HasForeignKey(e => e.ReceivableLossAllowanceAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.ImpairmentExpenseAccount).WithMany().HasForeignKey(e => e.ImpairmentExpenseAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.BadDebtRecoveryAccount).WithMany().HasForeignKey(e => e.BadDebtRecoveryAccountId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<SalesInvoiceHeader>(entity =>
            {
                entity.Property(e => e.CreditedAmount).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.WrittenOffAmount).HasColumnType("decimal(18, 4)");
                entity.HasOne(e => e.ReceivableAccount).WithMany().HasForeignKey(e => e.ReceivableAccountId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<PurchaseInvoiceHeader>(entity =>
            {
                entity.Property(e => e.CreditedAmount).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.AdvanceAppliedAmount).HasColumnType("decimal(18, 4)");
            });

            builder.Entity<CustomerCreditNote>(entity =>
            {
                entity.Property(e => e.Subtotal).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.TaxAmount).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.TotalAmount).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.AppliedAmount).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.RefundedAmount).HasColumnType("decimal(18, 4)");
                entity.HasIndex(e => new { e.CompanyId, e.No }).IsUnique().HasFilter("[No] IS NOT NULL");
                entity.HasOne(e => e.Customer).WithMany().HasForeignKey(e => e.CustomerId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.SourceSalesInvoiceHeader).WithMany().HasForeignKey(e => e.SourceSalesInvoiceHeaderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.GeneralLedgerHeader).WithMany().HasForeignKey(e => e.GeneralLedgerHeaderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.VoidGeneralLedgerHeader).WithMany().HasForeignKey(e => e.VoidGeneralLedgerHeaderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.CustomerCreditLiabilityAccount).WithMany().HasForeignKey(e => e.CustomerCreditLiabilityAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.SalesReturnsAccount).WithMany().HasForeignKey(e => e.SalesReturnsAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.ReceivableAccount).WithMany().HasForeignKey(e => e.ReceivableAccountId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<CustomerCreditNoteLine>(entity =>
            {
                entity.Property(e => e.TaxRateSnapshot).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.Quantity).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.UnitPrice).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.DiscountAmount).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.Subtotal).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.TaxAmount).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.TotalAmount).HasColumnType("decimal(18, 4)");
                entity.HasOne(e => e.CustomerCreditNote).WithMany(c => c.Lines).HasForeignKey(e => e.CustomerCreditNoteId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.SourceSalesInvoiceLine).WithMany().HasForeignKey(e => e.SourceSalesInvoiceLineId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Item).WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Tax).WithMany().HasForeignKey(e => e.TaxId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.TaxAccount).WithMany().HasForeignKey(e => e.TaxAccountId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<SupplierCreditNote>(entity =>
            {
                entity.Property(e => e.Subtotal).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.TaxAmount).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.TotalAmount).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.AppliedAmount).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.RefundedAmount).HasColumnType("decimal(18, 4)");
                entity.HasIndex(e => new { e.CompanyId, e.No }).IsUnique().HasFilter("[No] IS NOT NULL");
                entity.HasOne(e => e.Vendor).WithMany().HasForeignKey(e => e.VendorId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.SourcePurchaseInvoiceHeader).WithMany().HasForeignKey(e => e.SourcePurchaseInvoiceHeaderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.GeneralLedgerHeader).WithMany().HasForeignKey(e => e.GeneralLedgerHeaderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.VoidGeneralLedgerHeader).WithMany().HasForeignKey(e => e.VoidGeneralLedgerHeaderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.SupplierCreditAssetAccount).WithMany().HasForeignKey(e => e.SupplierCreditAssetAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.PayableAccount).WithMany().HasForeignKey(e => e.PayableAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.ReturnClearingAccount).WithMany().HasForeignKey(e => e.ReturnClearingAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.ValuationDifferenceAccount).WithMany().HasForeignKey(e => e.ValuationDifferenceAccountId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<SupplierCreditNoteLine>(entity =>
            {
                entity.Property(e => e.TaxRateSnapshot).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.Quantity).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.UnitCost).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.Subtotal).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.TaxAmount).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.TotalAmount).HasColumnType("decimal(18, 4)");
                entity.HasOne(e => e.SupplierCreditNote).WithMany(c => c.Lines).HasForeignKey(e => e.SupplierCreditNoteId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.SourcePurchaseInvoiceLine).WithMany().HasForeignKey(e => e.SourcePurchaseInvoiceLineId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.MatchedReturnDispatch).WithMany().HasForeignKey(e => e.MatchedReturnDispatchId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Item).WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.ExpenseOrAssetAccount).WithMany().HasForeignKey(e => e.ExpenseOrAssetAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Tax).WithMany().HasForeignKey(e => e.TaxId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.TaxAccount).WithMany().HasForeignKey(e => e.TaxAccountId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<SupplierAdvance>(entity =>
            {
                entity.Property(e => e.Amount).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.AppliedAmount).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.RefundedAmount).HasColumnType("decimal(18, 4)");
                entity.HasIndex(e => new { e.CompanyId, e.No }).IsUnique().HasFilter("[No] IS NOT NULL");
                entity.HasOne(e => e.Vendor).WithMany().HasForeignKey(e => e.VendorId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.PurchaseOrderHeader).WithMany().HasForeignKey(e => e.PurchaseOrderHeaderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Bank).WithMany().HasForeignKey(e => e.BankId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.GeneralLedgerHeader).WithMany().HasForeignKey(e => e.GeneralLedgerHeaderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.VoidGeneralLedgerHeader).WithMany().HasForeignKey(e => e.VoidGeneralLedgerHeaderId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<SettlementApplication>(entity =>
            {
                entity.Property(e => e.Amount).HasColumnType("decimal(18, 4)");
                entity.HasIndex(e => new { e.CompanyId, e.SalesInvoiceHeaderId });
                entity.HasIndex(e => new { e.CompanyId, e.PurchaseInvoiceHeaderId });
                entity.HasOne(e => e.CustomerCreditNote).WithMany().HasForeignKey(e => e.CustomerCreditNoteId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.SupplierCreditNote).WithMany().HasForeignKey(e => e.SupplierCreditNoteId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.SupplierAdvance).WithMany().HasForeignKey(e => e.SupplierAdvanceId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.SalesInvoiceHeader).WithMany().HasForeignKey(e => e.SalesInvoiceHeaderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.PurchaseInvoiceHeader).WithMany().HasForeignKey(e => e.PurchaseInvoiceHeaderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.GeneralLedgerHeader).WithMany().HasForeignKey(e => e.GeneralLedgerHeaderId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<SettlementApplicationReversal>(entity =>
            {
                entity.HasIndex(e => e.SettlementApplicationId).IsUnique();
                entity.HasOne(e => e.SettlementApplication).WithOne(a => a.Reversal).HasForeignKey<SettlementApplicationReversal>(e => e.SettlementApplicationId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.GeneralLedgerHeader).WithMany().HasForeignKey(e => e.GeneralLedgerHeaderId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<PartyRefund>(entity =>
            {
                entity.Property(e => e.Amount).HasColumnType("decimal(18, 4)");
                entity.HasIndex(e => new { e.CompanyId, e.No }).IsUnique().HasFilter("[No] IS NOT NULL");
                entity.HasOne(e => e.Customer).WithMany().HasForeignKey(e => e.CustomerId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Vendor).WithMany().HasForeignKey(e => e.VendorId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Bank).WithMany().HasForeignKey(e => e.BankId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.GeneralLedgerHeader).WithMany().HasForeignKey(e => e.GeneralLedgerHeaderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.VoidGeneralLedgerHeader).WithMany().HasForeignKey(e => e.VoidGeneralLedgerHeaderId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<PartyRefundAllocation>(entity =>
            {
                entity.Property(e => e.Amount).HasColumnType("decimal(18, 4)");
                entity.HasOne(e => e.PartyRefund).WithMany(r => r.Allocations).HasForeignKey(e => e.PartyRefundId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.CustomerCreditNote).WithMany().HasForeignKey(e => e.CustomerCreditNoteId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.SupplierCreditNote).WithMany().HasForeignKey(e => e.SupplierCreditNoteId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.SupplierAdvance).WithMany().HasForeignKey(e => e.SupplierAdvanceId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<CustomerReturnReceipt>(entity =>
            {
                entity.HasIndex(e => new { e.CompanyId, e.No }).IsUnique().HasFilter("[No] IS NOT NULL");
                entity.HasOne(e => e.Customer).WithMany().HasForeignKey(e => e.CustomerId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.SalesInvoiceHeader).WithMany().HasForeignKey(e => e.SalesInvoiceHeaderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.CustomerCreditNote).WithMany().HasForeignKey(e => e.CustomerCreditNoteId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.GeneralLedgerHeader).WithMany().HasForeignKey(e => e.GeneralLedgerHeaderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.VoidGeneralLedgerHeader).WithMany().HasForeignKey(e => e.VoidGeneralLedgerHeaderId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<CustomerReturnReceiptLine>(entity =>
            {
                entity.Property(e => e.Quantity).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.OriginalUnitCost).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.RecoverableUnitCost).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.TotalRestoredCost).HasColumnType("decimal(18, 4)");
                entity.HasOne(e => e.CustomerReturnReceipt).WithMany(r => r.Lines).HasForeignKey(e => e.CustomerReturnReceiptId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.SalesInvoiceLine).WithMany().HasForeignKey(e => e.SalesInvoiceLineId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Item).WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.InventoryControlJournal).WithMany().HasForeignKey(e => e.InventoryControlJournalId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.InventoryAccount).WithMany().HasForeignKey(e => e.InventoryAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.CogsAccount).WithMany().HasForeignKey(e => e.CogsAccountId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<SupplierReturnDispatch>(entity =>
            {
                entity.HasIndex(e => new { e.CompanyId, e.No }).IsUnique().HasFilter("[No] IS NOT NULL");
                entity.HasOne(e => e.Vendor).WithMany().HasForeignKey(e => e.VendorId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.PurchaseReceiptHeader).WithMany().HasForeignKey(e => e.PurchaseReceiptHeaderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.PurchaseInvoiceHeader).WithMany().HasForeignKey(e => e.PurchaseInvoiceHeaderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.SupplierCreditNote).WithMany().HasForeignKey(e => e.SupplierCreditNoteId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.ReturnClearingAccount).WithMany().HasForeignKey(e => e.ReturnClearingAccountId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.GeneralLedgerHeader).WithMany().HasForeignKey(e => e.GeneralLedgerHeaderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.VoidGeneralLedgerHeader).WithMany().HasForeignKey(e => e.VoidGeneralLedgerHeaderId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<SupplierReturnDispatchLine>(entity =>
            {
                entity.Property(e => e.Quantity).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.CarryingUnitCost).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.TotalCarryingCost).HasColumnType("decimal(18, 4)");
                entity.HasOne(e => e.SupplierReturnDispatch).WithMany(d => d.Lines).HasForeignKey(e => e.SupplierReturnDispatchId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.PurchaseReceiptLine).WithMany().HasForeignKey(e => e.PurchaseReceiptLineId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.PurchaseInvoiceLine).WithMany().HasForeignKey(e => e.PurchaseInvoiceLineId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Item).WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.InventoryControlJournal).WithMany().HasForeignKey(e => e.InventoryControlJournalId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.InventoryAccount).WithMany().HasForeignKey(e => e.InventoryAccountId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ReceivableWriteOff>(entity =>
            {
                entity.Property(e => e.Amount).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.AllowanceAllocatedAmount).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.ImpairmentShortfallAmount).HasColumnType("decimal(18, 4)");
                entity.Property(e => e.RecoveredAmount).HasColumnType("decimal(18, 4)");
                entity.HasIndex(e => new { e.CompanyId, e.No }).IsUnique().HasFilter("[No] IS NOT NULL");
                entity.HasOne(e => e.Customer).WithMany().HasForeignKey(e => e.CustomerId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.SalesInvoiceHeader).WithMany().HasForeignKey(e => e.SalesInvoiceHeaderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.GeneralLedgerHeader).WithMany().HasForeignKey(e => e.GeneralLedgerHeaderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.VoidGeneralLedgerHeader).WithMany().HasForeignKey(e => e.VoidGeneralLedgerHeaderId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<WriteOffRecovery>(entity =>
            {
                entity.Property(e => e.Amount).HasColumnType("decimal(18, 4)");
                entity.HasIndex(e => new { e.CompanyId, e.No }).IsUnique().HasFilter("[No] IS NOT NULL");
                entity.HasOne(e => e.ReceivableWriteOff).WithMany(w => w.Recoveries).HasForeignKey(e => e.ReceivableWriteOffId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Bank).WithMany().HasForeignKey(e => e.BankId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.GeneralLedgerHeader).WithMany().HasForeignKey(e => e.GeneralLedgerHeaderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.VoidGeneralLedgerHeader).WithMany().HasForeignKey(e => e.VoidGeneralLedgerHeaderId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<SettlementCommandReceipt>(entity =>
            {
                entity.HasIndex(e => new { e.CompanyId, e.OperationKey, e.IdempotencyKey })
                    .IsUnique()
                    .HasDatabaseName("UX_SettlementCommandReceipt_Company_Op_Key");
            });
        }
    }
}
