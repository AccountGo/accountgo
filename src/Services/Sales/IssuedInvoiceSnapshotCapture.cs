// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Sales;
using Core.Tenancy;
using Services.Administration;
using Services.Banking;

namespace Services.Sales
{
    public sealed class IssuedInvoiceSnapshotCapture : IIssuedInvoiceSnapshotCapture
    {
        private readonly IRepository<Company> _companies;
        private readonly IRepository<CompanySetting> _settings;
        private readonly IRepository<Customer> _customers;
        private readonly IRepository<Bank> _banks;
        private readonly IAdministrationService _admin;
        private readonly ITenantContext _tenant;

        public IssuedInvoiceSnapshotCapture(
            IRepository<Company> companies,
            IRepository<CompanySetting> settings,
            IRepository<Customer> customers,
            IRepository<Bank> banks,
            IAdministrationService admin,
            ITenantContext tenant)
        {
            _companies = companies;
            _settings = settings;
            _customers = customers;
            _banks = banks;
            _admin = admin;
            _tenant = tenant;
        }

        public void CaptureSellerAndBillToIfIssued(SalesInvoiceHeader invoice)
        {
            ArgumentNullException.ThrowIfNull(invoice);
            invoice.CaptureSellerIfIssued(CurrentCompany(), _settings.Table.FirstOrDefault());
            if (invoice.BillToJson != null)
                return;

            var customer = _customers.GetAllIncluding(c => c.Party!, c => c.BillToContact!)
                .FirstOrDefault(c => c.Id == invoice.CustomerId);
            invoice.CaptureBillToIfIssued(customer?.Party, customer?.BillToContact);
        }

        public bool CapturePaymentInstructionsIfIssued(SalesInvoiceHeader invoice)
        {
            ArgumentNullException.ThrowIfNull(invoice);
            if (invoice.Status is null or SalesInvoiceStatus.Draft or SalesInvoiceStatus.Void)
                return false;

            var setting = _settings.Table.FirstOrDefault();
            var invoiceSettings = setting?.GetSalesInvoiceSettings() ?? new SalesInvoiceSettings();
            var paymentBank = invoiceSettings.PaymentBankId.HasValue
                ? _banks.Table.FirstOrDefault(b => b.Id == invoiceSettings.PaymentBankId.Value)
                : null;
            var sellerName = InvoiceSeller.FromJson(invoice.SellerJson)?.Name;

            var resolved = BankPaymentInstructions.Resolve(
                invoice.PaymentInstructions, invoiceSettings, paymentBank, sellerName, invoice.No);
            var before = invoice.PaymentInstructions;
            invoice.CapturePaymentInstructionsIfIssued(resolved);
            return !string.Equals(before, invoice.PaymentInstructions, StringComparison.Ordinal);
        }

        private Company? CurrentCompany()
        {
            if (!string.IsNullOrWhiteSpace(_tenant.CompanySlug))
            {
                var bySlug = _admin.GetCompanyBySlug(_tenant.CompanySlug);
                if (bySlug != null)
                    return bySlug;
            }

            if (_tenant.CompanyId is int id)
                return _companies.Table.FirstOrDefault(c => c.Id == id)
                    ?? _admin.GetCompanies().FirstOrDefault(c => c.Id == id);
            return null;
        }
    }
}
