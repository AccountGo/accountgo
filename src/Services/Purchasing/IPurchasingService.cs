//-----------------------------------------------------------------------
// <copyright file="IPurchasingService.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Purchases;
using System;
using System.Collections.Generic;

namespace Services.Purchasing
{
    public partial interface IPurchasingService
    {
        void AddPurchaseInvoice(PurchaseInvoiceHeader purchaseIvoice, int? purchaseOrderId);
        void AddPurchaseOrder(PurchaseOrderHeader purchaseOrder, bool toSave);
        void UpdatePurchaseOrder(PurchaseOrderHeader purchaseOrder);
        void AddPurchaseOrderReceipt(PurchaseReceiptHeader purchaseOrderReceipt);
        IEnumerable<Vendor> GetVendors();
        Vendor GetVendorById(int id);
        IEnumerable<PurchaseOrderHeader> GetPurchaseOrders();
        PurchaseOrderHeader GetPurchaseOrderById(int id);
        PurchaseOrderLine GetPurchaseOrderLineById(int id);
        PurchaseReceiptHeader GetPurchaseReceiptById(int id);
        void AddVendor(Vendor vendor);
        void UpdateVendor(Vendor vendor);
        IEnumerable<PurchaseInvoiceHeader> GetPurchaseInvoices();
        PurchaseInvoiceHeader GetPurchaseInvoiceById(int id);
        /// <param name="preserveTaxAmounts">
        /// When true, keeps each line's supplied <see cref="PurchaseInvoiceLine.TaxAmount"/> instead of
        /// recalculating tax-on-top from the rate (needed for tax-inclusive bank amounts).
        /// </param>
        void SaveBill(PurchaseInvoiceHeader bill, bool preserveTaxAmounts = false);
        int DefaultLineAccountId(int itemId, Vendor vendor);
        void ApproveBill(int billId, string? approvedBy = null, DateTime? approvedAt = null);
        void VoidBill(int billId, DateTime voidDate, string? voidReason = null, string? voidedBy = null);
        VendorPayment PayBill(int billId, int accountId, decimal amount, DateTime date,
            string? paymentMethod = null, string? referenceNo = null, string? memo = null);
        VendorPayment VoidPayment(int paymentId, DateTime voidDate, string? voidReason = null, string? voidedBy = null);
    }
}
