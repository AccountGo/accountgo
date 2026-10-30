// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Core.Domain.Sales;

namespace Services.Sales
{
    /// <summary>
    /// Shared receipt + allocation path for RecordPayment and ApplyOpenInvoice (#436).
    /// </summary>
    public interface IInvoicePaymentRecorder
    {
        /// <summary>
        /// Posts a sales receipt that credits AR, allocates it to the invoice, and refreshes payment status.
        /// Caller persists the invoice afterward.
        /// </summary>
        SalesReceiptHeader RecordAllocatedReceipt(
            SalesInvoiceHeader invoice,
            decimal amount,
            DateTime paymentDate,
            int accountToDebitId,
            int accountToCreditId,
            string? paymentMethod,
            string? referenceNo,
            string? memo);
    }
}
