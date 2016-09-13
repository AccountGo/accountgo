//-----------------------------------------------------------------------
// <copyright file="ISalesService.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain;
using Core.Domain.Sales;
using System.Collections.Generic;

namespace Services.Sales
{
    public partial interface ISalesService
    {
        void AddSalesOrder(SalesOrderHeader salesOrder, bool toSave);
        void UpdateSalesOrder(SalesOrderHeader salesOrder);
        void AddSalesInvoice(SalesInvoiceHeader salesInvoice, int? salesDeliveryId, int? salesOrderId);
        void AddSalesReceipt(SalesReceiptHeader salesReceipt);
        void AddSalesReceiptNoInvoice(SalesReceiptHeader salesReceipt);
        void AddSalesDelivery(SalesDeliveryHeader salesDelivery, bool toSave);
        IEnumerable<SalesInvoiceHeader> GetSalesInvoices();
        SalesInvoiceHeader GetSalesInvoiceById(int id);
        SalesInvoiceHeader GetSalesInvoiceByNo(string no);
        void UpdateSalesInvoice(SalesInvoiceHeader salesInvoice);
        IEnumerable<SalesReceiptHeader> GetSalesReceipts();
        SalesReceiptHeader GetSalesReceiptById(int id);
        void UpdateSalesReceipt(SalesReceiptHeader salesReceipt);
        IEnumerable<Customer> GetCustomers();
        Customer GetCustomerById(int id);
        void UpdateCustomer(Customer customer);
        void AddCustomer(Customer customer);
        ICollection<SalesReceiptHeader> GetCustomerReceiptsForAllocation(int customerId);
        void SaveCustomerAllocation(CustomerAllocation allocation);
        IEnumerable<SalesDeliveryHeader> GetSalesDeliveries();
        IEnumerable<SalesOrderHeader> GetSalesOrders();
        SalesOrderHeader GetSalesOrderById(int id);
        SalesOrderLine GetSalesOrderLineById(int id);
        SalesDeliveryHeader GetSalesDeliveryById(int id);
        IEnumerable<Contact> GetContacts();
        void SaveContact(Contact contact);
        ICollection<SalesInvoiceHeader> GetSalesInvoicesByCustomerId(int customerId, SalesInvoiceStatus status);
        ICollection<CustomerAllocation> GetCustomerAllocations(int customerId);
        void AddSalesQuote(SalesQuoteHeader salesQuoteHeader);
        void UpdateSalesQuote(SalesQuoteHeader salesQuoteHeader);
        IEnumerable<SalesQuoteHeader> GetSalesQuotes();
        IEnumerable<SalesInvoiceHeader> GetCustomerInvoices(int customerId);
        SalesQuoteHeader GetSalesQuotationById(int id);
        void SaveSalesInvoice(SalesInvoiceHeader salesInvoice, SalesOrderHeader salesOrder);
        void PostSalesInvoice(int invoiceId);
        Contact GetContacyById(int id);
        CustomerContact GetCustomerContact(int id);
        void BookQuotation(int id);
    }
}
