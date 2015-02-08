using Core.Domain;
using Core.Domain.Sales;
using System.Collections.Generic;

namespace Services.Sales
{
    public partial interface ISalesService
    {
        void AddSalesOrder(SalesOrderHeader salesOrder, bool toSave);
        void UpdateSalesOrder(SalesOrderHeader salesOrder);
        void AddSalesInvoice(SalesInvoiceHeader salesInvoice, int? salesOrderId);
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
        IEnumerable<PaymentTerm> GetPaymentTerms();
        IEnumerable<SalesDeliveryHeader> GetSalesDeliveries();
        IEnumerable<SalesOrderHeader> GetSalesOrders();
        SalesOrderHeader GetSalesOrderById(int id);
        SalesDeliveryHeader GetSalesDeliveryById(int id);
        IEnumerable<Contact> GetContacts();
        int SaveContact(Contact contact);
    }
}
