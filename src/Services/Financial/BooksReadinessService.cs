// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Purchases;
using Core.Domain.Sales;

namespace Services.Financial
{
    public sealed class BooksReadinessService : IBooksReadinessService
    {
        private readonly IRepository<SalesInvoiceHeader> _invoices;
        private readonly IRepository<PurchaseInvoiceHeader> _bills;

        public BooksReadinessService(IRepository<SalesInvoiceHeader> invoices, IRepository<PurchaseInvoiceHeader> bills)
        {
            _invoices = invoices;
            _bills = bills;
        }

        public BooksReadiness Check()
        {
            // Issued = anything past Draft that is not Void. Repositories are tenant-filtered.
            var invoices = _invoices.Table
                .Where(i => i.Status != null
                    && i.Status != SalesInvoiceStatus.Draft
                    && i.Status != SalesInvoiceStatus.Void
                    && i.GeneralLedgerHeaderId == null)
                .OrderBy(i => i.Date).ThenBy(i => i.Id)
                .Select(i => new UnpostedDocument("invoice", i.Id, i.No, i.Date))
                .ToList();

            var bills = _bills.Table
                .Where(b => b.Status != null
                    && b.Status != PurchaseInvoiceStatus.Draft
                    && b.Status != PurchaseInvoiceStatus.Void
                    && b.GeneralLedgerHeaderId == null)
                .OrderBy(b => b.Date).ThenBy(b => b.Id)
                .Select(b => new UnpostedDocument("bill", b.Id, b.No, b.Date))
                .ToList();

            return new BooksReadiness(invoices, bills);
        }
    }
}
