using System;
using System.Collections.Generic;
using System.Linq;
using Api.Data;
using Microsoft.EntityFrameworkCore;
using Core.Tenancy;
using Services.Administration;
using Services.Sales;

namespace Api.Service
{
    /// <summary>
    /// Builds the sales-invoice DTO used by the UI, PDF, and customer email — same shape everywhere.
    /// </summary>
    public class SalesInvoicePresentation
    {
        private readonly ISalesService _sales;
        private readonly IAdministrationService _admin;
        private readonly ApiDbContext _db;
        private readonly ITenantContext _tenant;

        public SalesInvoicePresentation(
            ISalesService sales,
            IAdministrationService admin,
            ApiDbContext db,
            ITenantContext tenant)
        {
            _sales = sales;
            _admin = admin;
            _db = db;
            _tenant = tenant;
        }

        public Dto.Sales.SalesInvoice? Get(int id)
        {
            var salesInvoice = _sales.GetSalesInvoiceById(id);
            if (salesInvoice == null)
                return null;

            var paymentTerm = _admin.GetPaymentTerms().FirstOrDefault(t => t.Id == salesInvoice.PaymentTermId);
            var dueDate = salesInvoice.ResolveDueDate(paymentTerm);

            var defaultCompany = CurrentCompany();
            var setting = _db.CompanySettings.FirstOrDefault();
            var invoiceSettings = setting?.GetSalesInvoiceSettings() ?? new Core.Domain.SalesInvoiceSettings();
            var seller = Core.Domain.Sales.InvoiceSeller.FromJson(salesInvoice.SellerJson)
                ?? Core.Domain.Sales.InvoiceSeller.FromWorkspace(defaultCompany, setting);

            var party = salesInvoice.Customer?.Party;
            var billToContact = salesInvoice.Customer?.BillToContact;
            var billTo = Core.Domain.Sales.InvoiceBillTo.FromJson(salesInvoice.BillToJson)
                ?? Core.Domain.Sales.InvoiceBillTo.FromParty(party, billToContact);

            // Invoices go to the company's email on record (often an accounts-payable inbox), falling back to the frozen
            // bill-to so issued invoices stay sendable after the company email is cleared. The contact named in the
            // "Attn:" line is copied, or receives it when the company has no email at all.
            var attentionEmail = Blank(AttentionContact(billTo.ContactId, billToContact)?.Party?.Email);
            var recipientEmail = Blank(party?.Email) ?? Blank(billTo.Email) ?? attentionEmail;
            var ccEmail = string.Equals(attentionEmail, recipientEmail, StringComparison.OrdinalIgnoreCase) ? null : attentionEmail;

            Core.Domain.Financials.Bank? paymentBank = invoiceSettings.PaymentBankId.HasValue
                ? _db.Banks.FirstOrDefault(b => b.Id == invoiceSettings.PaymentBankId.Value)
                : null;

            var dto = new Dto.Sales.SalesInvoice
            {
                Id = salesInvoice.Id,
                CustomerId = salesInvoice.CustomerId,
                CustomerName = billTo.Name,
                CustomerAttention = billTo.Attention,
                CustomerEmail = billTo.Email,
                CustomerPhone = billTo.Phone,
                CustomerAddress = billTo.AddressText,
                RecipientEmail = recipientEmail,
                RecipientCcEmail = ccEmail,
                No = salesInvoice.No,
                InvoiceDate = salesInvoice.Date,
                DueDate = dueDate,
                SalesInvoiceLines = new List<Dto.Sales.SalesInvoiceLine>(),
                PaymentTermId = salesInvoice.PaymentTermId,
                ReferenceNo = salesInvoice.ReferenceNo,
                ShippingHandlingCharge = salesInvoice.ShippingHandlingCharge,
                Notes = salesInvoice.Notes,
                PaymentInstructions = salesInvoice.PaymentInstructions,
                EffectivePaymentInstructions = Services.Banking.BankPaymentInstructions.ForPresentation(
                    salesInvoice.Status is not (null or Core.Domain.SalesInvoiceStatus.Draft),
                    salesInvoice.PaymentInstructions, invoiceSettings, paymentBank, seller.Name, salesInvoice.No),
                Posted = salesInvoice.GeneralLedgerHeaderId != null,
                CompanyName = defaultCompany?.Name,
                Seller = new Dto.Sales.SellerInfo
                {
                    Name = seller.Name,
                    TaxId = seller.TaxId,
                    AddressLines = invoiceSettings.ShowSellerAddress ? seller.AddressLines : new List<string>(),
                    Email = seller.Email,
                    Phone = seller.Phone
                },
                Display = new Dto.Sales.DisplayFlags
                {
                    ShowCustomerEmail = invoiceSettings.ShowCustomerEmail,
                    ShowAttention = invoiceSettings.ShowAttention,
                    ShowNotes = invoiceSettings.ShowNotes,
                    ShowPaymentInstructions = invoiceSettings.ShowPaymentInstructions
                }
            };

            if (salesInvoice.CustomerAllocations != null)
            {
                dto.Payments = salesInvoice.CustomerAllocations
                    .OrderByDescending(a => a.Date)
                    .Select(a => new Dto.Sales.InvoicePaymentItemDto
                    {
                        Id = a.Id,
                        ReceiptNo = a.SalesReceiptHeader?.No,
                        Date = a.Date,
                        Amount = a.Amount,
                        PaymentMethod = a.SalesReceiptHeader?.PaymentMethod ?? "Payment",
                        ReferenceNo = a.SalesReceiptHeader?.ReferenceNo,
                        Notes = a.SalesReceiptHeader?.Memo
                    }).ToList();
            }

            dto.Emails = _db.SalesInvoiceEmails
                .Where(e => e.SalesInvoiceHeaderId == salesInvoice.Id)
                .OrderByDescending(e => e.SentAt)
                .AsEnumerable()
                .Select(e => new Dto.Sales.InvoiceEmailItemDto
                {
                    Id = e.Id,
                    Kind = e.Kind,
                    To = e.ToEmail,
                    SentAt = DateTime.SpecifyKind(e.SentAt, DateTimeKind.Utc),
                    SentBy = e.SentByEmail,
                    DispatchId = e.DispatchId,
                    FromEmail = e.FromEmail,
                })
                .ToList();

            foreach (var line in salesInvoice.SalesInvoiceLines)
            {
                dto.SalesInvoiceLines.Add(new Dto.Sales.SalesInvoiceLine
                {
                    Id = line.Id,
                    Amount = line.Amount,
                    Discount = line.Discount,
                    Quantity = line.Quantity,
                    ItemId = line.ItemId,
                    MeasurementId = line.MeasurementId,
                    ItemDescription = line.DisplayDescription,
                    MeasurementDescription = line.Measurement?.Description ?? "EA",
                    LineTotal = line.Subtotal,
                    TaxId = line.TaxId,
                    TaxAmount = line.TaxAmount,
                    SalesAccountId = line.SalesAccountId,
                    SalesAccountCode = line.SalesAccount?.AccountCode,
                    SalesAccountName = line.SalesAccount?.AccountName
                });
            }

            ApplySummary(dto, Summarize(salesInvoice, dueDate));

            if (!dto.Posted && dto.SalesInvoiceLines.Count >= 1)
                dto.ReadyForPosting = true;

            return dto;
        }

        private Core.Domain.Company? CurrentCompany() =>
            (!string.IsNullOrWhiteSpace(_tenant.CompanySlug) ? _admin.GetCompanyBySlug(_tenant.CompanySlug) : null)
            ?? _admin.GetCompanies().FirstOrDefault(c => c.Id == _tenant.CompanyId);

        /// <summary>The contact the invoice's "Attn:" line names: the frozen one on issued invoices, else the client's Bill To contact.</summary>
        private Core.Domain.Contact? AttentionContact(int? contactId, Core.Domain.Contact? billToContact)
        {
            if (contactId == null)
                return null;
            if (billToContact?.Id == contactId)
                return billToContact;
            return _db.Contacts.Include(c => c.Party).FirstOrDefault(c => c.Id == contactId);
        }

        private static string? Blank(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static (decimal TotalTax, decimal GrandTotal, decimal Allocated, decimal BalanceDue,
            string Status, Core.Domain.SalesInvoiceStatus StatusId) Summarize(
            Core.Domain.Sales.SalesInvoiceHeader invoice, DateTime dueDate)
        {
            var tax = invoice.ComputeTotalTax();
            var grandTotal = invoice.GrandTotal();
            var allocated = Math.Round(invoice.CustomerAllocations?.Sum(a => a.Amount) ?? 0, 2, MidpointRounding.AwayFromZero);
            var balanceDue = Math.Round(Math.Max(0, grandTotal - allocated), 2, MidpointRounding.AwayFromZero);

            var (status, statusId) =
                invoice.Status == Core.Domain.SalesInvoiceStatus.Void ? ("Void", Core.Domain.SalesInvoiceStatus.Void)
                : invoice.Status == Core.Domain.SalesInvoiceStatus.Closed || (balanceDue <= 0.005m && allocated > 0) ? ("Paid", Core.Domain.SalesInvoiceStatus.Closed)
                : allocated > 0 && balanceDue > 0.005m ? ("Partially Paid", Core.Domain.SalesInvoiceStatus.Open)
                : invoice.Status == Core.Domain.SalesInvoiceStatus.Draft && allocated == 0 ? ("Draft", Core.Domain.SalesInvoiceStatus.Draft)
                : dueDate.Date < DateTime.UtcNow.Date && balanceDue > 0.005m ? ("Overdue", Core.Domain.SalesInvoiceStatus.Overdue)
                : ("Open", Core.Domain.SalesInvoiceStatus.Open);

            if (statusId == Core.Domain.SalesInvoiceStatus.Void)
                balanceDue = 0;

            return (tax, grandTotal, allocated, balanceDue, status, statusId);
        }

        private static void ApplySummary(
            Dto.Sales.SalesInvoice dto,
            (decimal TotalTax, decimal GrandTotal, decimal Allocated, decimal BalanceDue, string Status, Core.Domain.SalesInvoiceStatus StatusId) summary)
        {
            dto.TotalTax = summary.TotalTax;
            dto.TotalAmountAfterTax = summary.GrandTotal;
            dto.TotalAllocatedAmount = summary.Allocated;
            dto.PaidAmount = summary.Allocated;
            dto.BalanceDue = summary.BalanceDue;
            dto.Status = summary.Status;
            dto.StatusId = (int)summary.StatusId;
        }
    }
}
