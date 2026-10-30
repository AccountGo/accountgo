// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Core.Domain.Sales;

namespace Services.Sales
{
    public sealed class InvoicePaymentRecorder : IInvoicePaymentRecorder
    {
        private readonly ISalesService _sales;

        public InvoicePaymentRecorder(ISalesService sales)
        {
            _sales = sales;
        }

        public SalesReceiptHeader RecordAllocatedReceipt(
            SalesInvoiceHeader invoice,
            decimal amount,
            DateTime paymentDate,
            int accountToDebitId,
            int accountToCreditId,
            string? paymentMethod,
            string? referenceNo,
            string? memo)
        {
            ArgumentNullException.ThrowIfNull(invoice);

            var receipt = new SalesReceiptHeader
            {
                CustomerId = invoice.CustomerId,
                Date = paymentDate,
                Amount = amount,
                AccountToDebitId = accountToDebitId,
                PaymentMethod = string.IsNullOrWhiteSpace(paymentMethod) ? "Bank Transfer" : paymentMethod.Trim(),
                ReferenceNo = string.IsNullOrWhiteSpace(referenceNo) ? invoice.No : referenceNo.Trim(),
                Memo = memo
            };
            receipt.SalesReceiptLines.Add(new SalesReceiptLine
            {
                Quantity = 1,
                Amount = amount,
                AmountPaid = amount,
                AccountToCreditId = accountToCreditId
            });

            _sales.AddSalesReceipt(receipt);

            invoice.CustomerAllocations.Add(new CustomerAllocation
            {
                CustomerId = invoice.CustomerId,
                Date = paymentDate,
                SalesInvoiceHeaderId = invoice.Id,
                SalesReceiptHeaderId = receipt.Id,
                Amount = amount
            });
            invoice.RefreshPaymentStatus();
            return receipt;
        }
    }
}
