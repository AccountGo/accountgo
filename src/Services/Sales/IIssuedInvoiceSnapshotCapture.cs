// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Core.Domain.Sales;

namespace Services.Sales
{
    /// <summary>
    /// Captures seller, Bill To, and payment instructions when an invoice is issued —
    /// shared by SalesController and statement other-income (#436 I5).
    /// </summary>
    public interface IIssuedInvoiceSnapshotCapture
    {
        /// <summary>Records seller and Bill To as they print, once the invoice is issued.</summary>
        void CaptureSellerAndBillToIfIssued(SalesInvoiceHeader invoice);

        /// <summary>
        /// Stores resolved payment instructions after the seller is recorded and the number is assigned.
        /// </summary>
        /// <returns>True when <see cref="SalesInvoiceHeader.PaymentInstructions"/> changed.</returns>
        bool CapturePaymentInstructionsIfIssued(SalesInvoiceHeader invoice);
    }
}
