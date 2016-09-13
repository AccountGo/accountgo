//-----------------------------------------------------------------------
// <copyright file="SalesService.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Sales;
using Services.Financial;
using Services.Inventory;
using System.Linq;
using System;
using System.Collections.Generic;
using Core.Domain.TaxSystem;

namespace Services.Sales
{
    public partial class SalesService : BaseService, ISalesService
    {
        private readonly IFinancialService _financialService;
        private readonly IInventoryService _inventoryService;

        private readonly IRepository<SalesOrderHeader> _salesOrderRepo;
        private readonly IRepository<SalesOrderLine> _salesOrderLineRepo;
        private readonly IRepository<SalesInvoiceHeader> _salesInvoiceRepo;
        private readonly IRepository<SalesReceiptHeader> _salesReceiptRepo;
        private readonly IRepository<Customer> _customerRepo;
        private readonly IRepository<CustomerContact> _customerContactRepo;
        private readonly IRepository<VendorContact> _vendorContactRepo;
        private readonly IRepository<Account> _accountRepo;
        private readonly IRepository<Item> _itemRepo;
        private readonly IRepository<Measurement> _measurementRepo;
        private readonly IRepository<SequenceNumber> _sequenceNumberRepo;
        private readonly IRepository<PaymentTerm> _paymentTermRepo;
        private readonly IRepository<SalesDeliveryHeader> _salesDeliveryRepo;
        private readonly IRepository<Bank> _bankRepo;
        private readonly IRepository<GeneralLedgerSetting> _genetalLedgerSetting;
        private readonly IRepository<Contact> _contactRepo;
        private readonly IRepository<TaxGroup> _taxGroupRepo;
        private readonly IRepository<SalesQuoteHeader> _salesQuoteRepo;
        private readonly ISalesOrderRepository _salesOrderRepository;

        public SalesService(IFinancialService financialService,
            IInventoryService inventoryService,
            IRepository<SalesOrderHeader> salesOrderRepo,
            IRepository<SalesOrderLine> salesOrderLineRepo,
            IRepository<SalesInvoiceHeader> salesInvoiceRepo,
            IRepository<SalesReceiptHeader> salesReceiptRepo,
            IRepository<Customer> customerRepo,
            IRepository<Account> accountRepo,
            IRepository<Item> itemRepo,
            IRepository<Measurement> measurementRepo,
            IRepository<SequenceNumber> sequenceNumberRepo,
            IRepository<PaymentTerm> paymentTermRepo,
            IRepository<SalesDeliveryHeader> salesDeliveryRepo,
            IRepository<Bank> bankRepo,
            IRepository<GeneralLedgerSetting> generalLedgerSetting,
            IRepository<Contact> contactRepo,
            IRepository<TaxGroup> taxGroupRepo,
            IRepository<SalesQuoteHeader> salesQuoteRepo,
            ISalesOrderRepository salesOrderRepository,
            IRepository<CustomerContact> customerContactRepo,
            IRepository<VendorContact> vendorContactRepo)
            : base(sequenceNumberRepo, generalLedgerSetting, paymentTermRepo, bankRepo)
        {
            _financialService = financialService;
            _inventoryService = inventoryService;
            _salesOrderRepo = salesOrderRepo;
            _salesInvoiceRepo = salesInvoiceRepo;
            _salesReceiptRepo = salesReceiptRepo;
            _customerRepo = customerRepo;
            _accountRepo = accountRepo;
            _itemRepo = itemRepo;
            _measurementRepo = measurementRepo;
            _sequenceNumberRepo = sequenceNumberRepo;
            _paymentTermRepo = paymentTermRepo;
            _salesDeliveryRepo = salesDeliveryRepo;
            _bankRepo = bankRepo;
            _genetalLedgerSetting = generalLedgerSetting;
            _contactRepo = contactRepo;
            _taxGroupRepo = taxGroupRepo;
            _salesQuoteRepo = salesQuoteRepo;
            _salesOrderRepository = salesOrderRepository;
            _salesOrderLineRepo = salesOrderLineRepo;
            _customerContactRepo = customerContactRepo;
            _vendorContactRepo = vendorContactRepo;
        }

        public void AddSalesOrder(SalesOrderHeader salesOrder, bool toSave)
        {
            if (string.IsNullOrEmpty(salesOrder.No))
                salesOrder.No = GetNextNumber(SequenceNumberTypes.SalesOrder).ToString();
            if (toSave)
                _salesOrderRepo.Insert(salesOrder);
        }

        public void UpdateSalesOrder(SalesOrderHeader salesOrder)
        {
            var persisted = _salesOrderRepo.GetById(salesOrder.Id);
            foreach (var persistedLine in persisted.SalesOrderLines)
            {
                bool found = false;
                foreach (var currentLine in salesOrder.SalesOrderLines)
                {
                    if (persistedLine.Id == currentLine.Id)
                    {
                        found = true;
                        break;
                    }
                }

                if (found)
                    continue;
                else
                {

                }
            }
            _salesOrderRepo.Update(salesOrder);
        }

        public void AddSalesInvoice(SalesInvoiceHeader salesInvoice, int? salesDeliveryId, int? salesOrderId)
        {
            decimal totalAmount = 0, totalDiscount = 0;

            var taxes = new List<KeyValuePair<int, decimal>>();
            var sales = new List<KeyValuePair<int, decimal>>();

            var glHeader = _financialService.CreateGeneralLedgerHeader(DocumentTypes.SalesInvoice, salesInvoice.Date, string.Empty);
            var customer = GetCustomerById(salesInvoice.CustomerId);

            foreach (var lineItem in salesInvoice.SalesInvoiceLines)
            {
                var item = _inventoryService.GetItemById(lineItem.ItemId);

                var lineAmount = lineItem.Quantity * lineItem.Amount;

                if (!item.GLAccountsValidated())
                    throw new Exception("Item is not correctly setup for financial transaction. Please check if GL accounts are all set.");

                var lineDiscountAmount = (lineItem.Discount / 100) * lineAmount;
                totalDiscount += lineDiscountAmount;

                var totalLineAmount = lineAmount - lineDiscountAmount;

                totalAmount += totalLineAmount;

                var lineTaxes = _financialService.ComputeOutputTax(salesInvoice.CustomerId, item.Id, lineItem.Quantity, lineItem.Amount, lineItem.Discount);

                foreach (var t in lineTaxes)
                    taxes.Add(t);

                var lineTaxAmount = lineTaxes != null && lineTaxes.Count > 0 ? lineTaxes.Sum(t => t.Value) : 0;
                totalLineAmount = totalLineAmount - lineTaxAmount;

                sales.Add(new KeyValuePair<int, decimal>(item.SalesAccountId.Value, totalLineAmount));

                if (item.ItemCategory.ItemType == ItemTypes.Purchased)
                {
                    lineItem.InventoryControlJournal = _inventoryService.CreateInventoryControlJournal(lineItem.ItemId,
                        lineItem.MeasurementId,
                        DocumentTypes.SalesInvoice,
                        null,
                        lineItem.Quantity,
                        lineItem.Quantity * item.Cost,
                        lineItem.Quantity * item.Price);
                }
            }

            totalAmount += salesInvoice.ShippingHandlingCharge;
            var debitCustomerAR = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, customer.AccountsReceivableAccount.Id, Math.Round(totalAmount, 2, MidpointRounding.ToEven));
            glHeader.GeneralLedgerLines.Add(debitCustomerAR);

            var groupedSalesAccount = from s in sales
                                      group s by s.Key into grouped
                                      select new
                                      {
                                          Key = grouped.Key,
                                          Value = grouped.Sum(s => s.Value)
                                      };

            foreach (var salesAccount in groupedSalesAccount)
            {
                var salesAmount = salesAccount.Value;
                var creditSalesAccount = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, salesAccount.Key, Math.Round(salesAmount, 2, MidpointRounding.ToEven));
                glHeader.GeneralLedgerLines.Add(creditSalesAccount);
            }

            if (taxes != null && taxes.Count > 0)
            {
                var groupedTaxes = from t in taxes
                                   group t by t.Key into grouped
                                   select new
                                   {
                                       Key = grouped.Key,
                                       Value = grouped.Sum(t => t.Value)
                                   };

                foreach (var tax in groupedTaxes)
                {
                    var tx = _financialService.GetTaxes().Where(t => t.Id == tax.Key).FirstOrDefault();
                    var creditSalesTaxAccount = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, tx.SalesAccountId.Value, Math.Round(tax.Value, 2, MidpointRounding.ToEven));
                    glHeader.GeneralLedgerLines.Add(creditSalesTaxAccount);
                }
            }

            if (totalDiscount > 0)
            {
                var salesDiscountAccount = base.GetGeneralLedgerSetting().SalesDiscountAccount;
                var creditSalesDiscountAccount = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, salesDiscountAccount.Id, Math.Round(totalDiscount, 2, MidpointRounding.ToEven));
                glHeader.GeneralLedgerLines.Add(creditSalesDiscountAccount);
            }

            if (salesInvoice.ShippingHandlingCharge > 0)
            {
                var shippingHandlingAccount = base.GetGeneralLedgerSetting().ShippingChargeAccount;
                var creditShippingHandlingAccount = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, shippingHandlingAccount.Id, Math.Round(salesInvoice.ShippingHandlingCharge, 2, MidpointRounding.ToEven));
                glHeader.GeneralLedgerLines.Add(creditShippingHandlingAccount);
            }

            if (_financialService.ValidateGeneralLedgerEntry(glHeader))
            {
                salesInvoice.GeneralLedgerHeader = glHeader;

                salesInvoice.No = GetNextNumber(SequenceNumberTypes.SalesInvoice).ToString();

                if (!salesDeliveryId.HasValue)
                {
                    var salesDelivery = new SalesDeliveryHeader()
                    {
                        CustomerId = salesInvoice.CustomerId,
                        Date = salesInvoice.Date,
                        //SalesOrderHeaderId = salesOrderId
                    };
                    foreach (var line in salesInvoice.SalesInvoiceLines)
                    {
                        var item = _itemRepo.GetById(line.ItemId);
                        salesDelivery.SalesDeliveryLines.Add(new SalesDeliveryLine()
                        {
                            ItemId = line.ItemId,
                            MeasurementId = line.MeasurementId,
                            Quantity = line.Quantity,
                            Discount = line.Discount,
                            Price = item.Cost.Value,
                        });
                    }
                    AddSalesDelivery(salesDelivery, false);
                    //salesInvoice.SalesDeliveryHeader = salesDelivery;
                }

                _salesInvoiceRepo.Insert(salesInvoice);
            }
        }

        public void AddSalesReceipt(SalesReceiptHeader salesReceipt)
        {
            var customer = _customerRepo.GetById(salesReceipt.CustomerId);
            var glHeader = _financialService.CreateGeneralLedgerHeader(DocumentTypes.SalesReceipt, salesReceipt.Date, string.Empty);
            var debit = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, salesReceipt.AccountToDebitId.Value, salesReceipt.SalesReceiptLines.Sum(i => i.AmountPaid));
            var credit = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, customer.AccountsReceivableAccountId.Value, salesReceipt.SalesReceiptLines.Sum(i => i.AmountPaid));
            glHeader.GeneralLedgerLines.Add(debit);
            glHeader.GeneralLedgerLines.Add(credit);

            if (_financialService.ValidateGeneralLedgerEntry(glHeader))
            {
                salesReceipt.GeneralLedgerHeader = glHeader;

                salesReceipt.No = GetNextNumber(SequenceNumberTypes.SalesReceipt).ToString();
                _salesReceiptRepo.Insert(salesReceipt);
            }
        }

        /// <summary>
        /// Customer advances. Initial recognition. Debit to cash (asset), credit to customer advances (liability)
        /// </summary>
        /// <param name="salesReceipt"></param>
        public void AddSalesReceiptNoInvoice(SalesReceiptHeader salesReceipt)
        {
            var customer = _customerRepo.GetById(salesReceipt.CustomerId);
            var glHeader = _financialService.CreateGeneralLedgerHeader(DocumentTypes.SalesReceipt, salesReceipt.Date, string.Empty);
            var debit = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, salesReceipt.AccountToDebitId.Value, salesReceipt.Amount);
            glHeader.GeneralLedgerLines.Add(debit);

            foreach (var line in salesReceipt.SalesReceiptLines)
            {
                var credit = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, line.AccountToCreditId.Value, line.AmountPaid);
                glHeader.GeneralLedgerLines.Add(credit);
            }

            if (_financialService.ValidateGeneralLedgerEntry(glHeader))
            {
                salesReceipt.GeneralLedgerHeader = glHeader;

                salesReceipt.No = GetNextNumber(SequenceNumberTypes.SalesReceipt).ToString();
                _salesReceiptRepo.Insert(salesReceipt);
            }
        }

        public IEnumerable<SalesInvoiceHeader> GetSalesInvoices()
        {
            var query = _salesInvoiceRepo.GetAllIncluding(inv => inv.Customer,
                inv => inv.Customer.Party,
                inv => inv.SalesInvoiceLines);

            return query.AsEnumerable();
        }

        public SalesInvoiceHeader GetSalesInvoiceById(int id)
        {
            var invoice = _salesInvoiceRepo.GetAllIncluding(inv => inv.Customer,
                inv => inv.Customer.Party,
                inv => inv.Customer.CustomerAdvancesAccount,
                inv => inv.Customer.AccountsReceivableAccount,
                inv => inv.CustomerAllocations,
                inv => inv.SalesInvoiceLines)
                .Where(inv => inv.Id == id)
                .FirstOrDefault();

            return invoice;
        }

        public SalesInvoiceHeader GetSalesInvoiceByNo(string no)
        {
            var invoice = _salesInvoiceRepo.GetAllIncluding(inv => inv.Customer,
                inv => inv.Customer.Party,
                inv => inv.SalesInvoiceLines)
                .Where(inv => inv.No == no)
                .FirstOrDefault();

            return invoice;
        }

        public void UpdateSalesInvoice(SalesInvoiceHeader salesInvoice)
        {
            _salesInvoiceRepo.Update(salesInvoice);
        }

        public IEnumerable<SalesReceiptHeader> GetSalesReceipts()
        {
            var query = _salesReceiptRepo.GetAllIncluding(s => s.Customer,
                s => s.Customer.Party,
                s => s.CustomerAllocations,
                s => s.AccountToDebit,
                s => s.GeneralLedgerHeader,
                s => s.SalesReceiptLines);

            return query.AsEnumerable();
        }

        public SalesReceiptHeader GetSalesReceiptById(int id)
        {
            var receipt = _salesReceiptRepo.GetAllIncluding(r => r.Customer,
                r => r.CustomerAllocations,
                r => r.SalesReceiptLines,
                r => r.Customer.Party)
                .Where(r => r.Id == id).FirstOrDefault();

            return receipt;
        }

        public void UpdateSalesReceipt(SalesReceiptHeader salesReceipt)
        {
            _salesReceiptRepo.Update(salesReceipt);
        }

        public IEnumerable<Customer> GetCustomers()
        {
            System.Linq.Expressions.Expression<Func<Customer, object>>[] includeProperties = {
                c => c.Party,
                c => c.AccountsReceivableAccount,
                c => c.SalesInvoices,
                c => c.CustomerAdvancesAccount,
                c => c.SalesAccount,
                c => c.PromptPaymentDiscountAccount,
                c => c.PrimaryContact,
                c => c.PrimaryContact.Party,
                c => c.SalesInvoices,
                c => c.SalesReceipts,
                c => c.SalesOrders,
                c => c.TaxGroup,
            };

            var customers = _customerRepo.GetAllIncluding(includeProperties);

            foreach (var customer in customers)
            {
                foreach (var invoice in customer.SalesInvoices)
                {
                    invoice.SalesInvoiceLines = GetSalesInvoiceById(invoice.Id).SalesInvoiceLines;
                }

                foreach (var receipt in customer.SalesReceipts)
                {
                    receipt.SalesReceiptLines = GetSalesReceiptById(receipt.Id).SalesReceiptLines;
                }
            }

            return customers;
        }

        public Customer GetCustomerById(int id)
        {
            System.Linq.Expressions.Expression<Func<Customer, object>>[] includeProperties = {
                c => c.Party,
                c => c.AccountsReceivableAccount,
                c => c.SalesInvoices,
                c => c.CustomerAdvancesAccount,
                c => c.SalesAccount,
                c => c.PromptPaymentDiscountAccount,
                c => c.PrimaryContact,
                c => c.PrimaryContact.Party,
                c => c.SalesInvoices,
                c => c.SalesReceipts,
                c => c.SalesOrders,
                c => c.CustomerContact,
                c => c.SalesOrders
            };

            var customer = _customerRepo.GetAllIncluding(includeProperties)
                .Where(c => c.Id == id).FirstOrDefault();

            foreach (var customerContact in customer.CustomerContact)
            {
                var contact = GetContacyById(customerContact.ContactId);
                customerContact.Contact = contact;
            }

            foreach (var invoice in customer.SalesInvoices)
            {
                invoice.SalesInvoiceLines = GetSalesInvoiceById(invoice.Id).SalesInvoiceLines;
            }

            foreach (var receipt in customer.SalesReceipts)
            {
                receipt.SalesReceiptLines = GetSalesReceiptById(receipt.Id).SalesReceiptLines;
            }

            return customer;
        }

        public void UpdateCustomer(Customer customer)
        {
            _customerRepo.Update(customer);
        }

        public ICollection<SalesReceiptHeader> GetCustomerReceiptsForAllocation(int customerId)
        {
            var customerReceipts = _salesReceiptRepo.Table.Where(r => r.CustomerId == customerId);
            var customerReceiptsWithNoInvoice = new HashSet<SalesReceiptHeader>();
            foreach (var receipt in customerReceipts)
            {
                //if (receipt.SalesInvoiceHeaderId == null)
                //    customerReceiptsWithNoInvoice.Add(receipt);
                customerReceiptsWithNoInvoice.Add(receipt);
            }
            return customerReceiptsWithNoInvoice;
        }

        public void SaveCustomerAllocation(CustomerAllocation allocation)
        {
            //Revenue recognition. Debit the customer advances (liability) account and credit the revenue account.
            //In case of allocation, credit the accounts receivable since sales account is already credited from invoice.
            var invoice = GetSalesInvoiceById(allocation.SalesInvoiceHeaderId);
            var receipt = GetSalesReceiptById(allocation.SalesReceiptHeaderId);

            var glHeader = _financialService.CreateGeneralLedgerHeader(Core.Domain.DocumentTypes.CustomerAllocation, allocation.Date, string.Empty);

            foreach (var line in receipt.SalesReceiptLines)
            {
                Account accountToDebit = invoice.Customer.CustomerAdvancesAccount;
                var debit = _financialService.CreateGeneralLedgerLine(Core.Domain.DrOrCrSide.Dr, accountToDebit.Id, allocation.Amount);
                glHeader.GeneralLedgerLines.Add(debit);
            }

            Account accountToCredit = invoice.Customer.AccountsReceivableAccount;
            var credit = _financialService.CreateGeneralLedgerLine(Core.Domain.DrOrCrSide.Cr, accountToCredit.Id, allocation.Amount);
            glHeader.GeneralLedgerLines.Add(credit);

            if (_financialService.ValidateGeneralLedgerEntry(glHeader))
            {
                invoice.GeneralLedgerHeader = glHeader;
                invoice.CustomerAllocations.Add(allocation);
                _salesInvoiceRepo.Update(invoice);
            }
        }

        public void AddCustomer(Customer customer)
        {
            var accountAR = _accountRepo.Table.Where(e => e.AccountCode == "10120").FirstOrDefault();
            var accountSales = _accountRepo.Table.Where(e => e.AccountCode == "40100").FirstOrDefault();
            var accountAdvances = _accountRepo.Table.Where(e => e.AccountCode == "20120").FirstOrDefault();
            var accountSalesDiscount = _accountRepo.Table.Where(e => e.AccountCode == "40400").FirstOrDefault();

            customer.AccountsReceivableAccountId = accountAR != null ? (int?)accountAR.Id : null;
            customer.SalesAccountId = accountSales != null ? (int?)accountSales.Id : null;
            customer.CustomerAdvancesAccountId = accountAdvances != null ? (int?)accountAdvances.Id : null;
            customer.SalesDiscountAccountId = accountSalesDiscount != null ? (int?)accountSalesDiscount.Id : null;
            customer.TaxGroupId = _taxGroupRepo.Table.Where(tg => tg.Description == "VAT").FirstOrDefault().Id;

            customer.No = GetNextNumber(SequenceNumberTypes.Customer).ToString();
            _customerRepo.Insert(customer);
        }

        public IEnumerable<SalesDeliveryHeader> GetSalesDeliveries()
        {
            var query = from f in _salesDeliveryRepo.Table
                        select f;
            return query;
        }

        public void AddSalesDelivery(SalesDeliveryHeader salesDelivery, bool toSave)
        {
            var glHeader = _financialService.CreateGeneralLedgerHeader(DocumentTypes.SalesDelivery, salesDelivery.Date, string.Empty);
            // Debit = COGS, Credit = Inventory
            var debitAccounts = new List<KeyValuePair<int, decimal>>();
            var creditAccounts = new List<KeyValuePair<int, decimal>>();
            foreach (var line in salesDelivery.SalesDeliveryLines)
            {
                var item = _inventoryService.GetItemById(line.ItemId.Value);
                debitAccounts.Add(new KeyValuePair<int, decimal>(item.CostOfGoodsSoldAccountId.Value, item.Cost.Value * line.Quantity));
                creditAccounts.Add(new KeyValuePair<int, decimal>(item.InventoryAccountId.Value, item.Cost.Value * line.Quantity));
            }
            var groupedDebitAccounts = (from kvp in debitAccounts
                                        group kvp by kvp.Key into g
                                        select new KeyValuePair<int, decimal>(g.Key, g.Sum(e => e.Value)));
            var groupedCreditAccounts = (from kvp in creditAccounts
                                         group kvp by kvp.Key into g
                                         select new KeyValuePair<int, decimal>(g.Key, g.Sum(e => e.Value)));
            foreach (var account in groupedDebitAccounts)
            {
                glHeader.GeneralLedgerLines.Add(_financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, account.Key, account.Value));
            }
            foreach (var account in groupedCreditAccounts)
            {
                glHeader.GeneralLedgerLines.Add(_financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, account.Key, account.Value));
            }

            if (_financialService.ValidateGeneralLedgerEntry(glHeader))
            {
                salesDelivery.GeneralLedgerHeader = glHeader;

                salesDelivery.No = GetNextNumber(SequenceNumberTypes.SalesDelivery).ToString();

                //if(!salesDelivery.SalesOrderHeaderId.HasValue)
                //{
                //    var salesOrder = new SalesOrderHeader()
                //    {
                //        CustomerId = salesDelivery.CustomerId,
                //        PaymentTermId = salesDelivery.PaymentTermId,
                //        Date = salesDelivery.Date,
                //        No = GetNextNumber(SequenceNumberTypes.SalesOrder).ToString(),
                //    };

                //    foreach(var line in salesDelivery.SalesDeliveryLines)
                //    {
                //        var item = _inventoryService.GetItemById(line.ItemId.Value);
                //        salesOrder.SalesOrderLines.Add(new SalesOrderLine()
                //        {
                //            ItemId = item.Id,
                //            MeasurementId = line.MeasurementId.Value,
                //            Quantity = line.Quantity,
                //            Amount = item.Price.Value,
                //        });
                //    }
                //AddSalesOrder(salesOrder, false);
                //salesDelivery.SalesOrderHeader = salesOrder;
                //}

                if (toSave)
                    _salesDeliveryRepo.Insert(salesDelivery);
            }
        }

        public IEnumerable<SalesOrderHeader> GetSalesOrders()
        {
            var salesOrders = _salesOrderRepository.GetAllSalesOrders();
            return salesOrders;
        }

        public SalesOrderHeader GetSalesOrderById(int id)
        {
            var salesOrder = GetSalesOrders().FirstOrDefault(o => o.Id == id);

            if (salesOrder != null)
            {
                foreach (var line in salesOrder.SalesOrderLines)
                {
                    line.Item = _itemRepo.GetById(line.ItemId);
                    line.Measurement = _measurementRepo.GetById(line.MeasurementId);
                }

                return salesOrder;
            }
            return null;
        }

        public SalesOrderLine GetSalesOrderLineById(int id)
        {
            var salesOrderLine = _salesOrderLineRepo.GetAllIncluding(
                line => line.SalesOrderHeader,
                line => line.SalesOrderHeader.SalesOrderLines
                )
                .Where(line => line.Id == id)
                .FirstOrDefault();

            return salesOrderLine;
        }

        public SalesDeliveryHeader GetSalesDeliveryById(int id)
        {
            return _salesDeliveryRepo.GetById(id);
        }

        public IEnumerable<Contact> GetContacts()
        {
            var query = from f in _contactRepo.Table
                        select f;
            return query;
        }

        //public int SaveContact(Contact contact)
        //{
        //    _contactRepo.Insert(contact);
        //    return contact.Id;
        //}

        public void SaveContact(Contact contact)
        {
            try
            {
                if (contact.Id > 0)
                {
                    _contactRepo.Update(contact);
                 
                }
                else
                {
                    _contactRepo.Insert(contact);
                }
                
            }
            catch (Exception e)
            {

                throw e;
            }

        }

        public ICollection<SalesInvoiceHeader> GetSalesInvoicesByCustomerId(int customerId, SalesInvoiceStatus status)
        {
            var query = from invoice in _salesInvoiceRepo.Table
                        where invoice.CustomerId == customerId && invoice.Status == status
                        select invoice;
            return query.ToList();
        }

        public ICollection<CustomerAllocation> GetCustomerAllocations(int customerId)
        {
            return null;
        }

        public void AddSalesQuote(SalesQuoteHeader salesQuoteHeader)
        {
            salesQuoteHeader.No = GetNextNumber(SequenceNumberTypes.SalesQuote).ToString();
            _salesQuoteRepo.Insert(salesQuoteHeader);
        }

        public void UpdateSalesQuote(SalesQuoteHeader salesQuoteHeader)
        {
            _salesQuoteRepo.Update(salesQuoteHeader);
        }

        public IEnumerable<SalesQuoteHeader> GetSalesQuotes()
        {
            var quotes = _salesQuoteRepo
                .GetAllIncluding(line => line.SalesQuoteLines, p => p.Customer.Party)
                .AsEnumerable();
            return quotes;
        }

        public IEnumerable<SalesInvoiceHeader> GetCustomerInvoices(int customerId)
        {
            var invoices = _salesInvoiceRepo.GetAllIncluding(i => i.SalesInvoiceLines,
                i => i.CustomerAllocations)
                .Where(i => i.CustomerId == customerId);

            return invoices;
        }

        public SalesQuoteHeader GetSalesQuotationById(int id)
        {
            var quotation = _salesQuoteRepo.GetAllIncluding(q => q.Customer,
                q => q.Customer.Party,
                q => q.SalesQuoteLines)
                .Where(q => q.Id == id)
                .FirstOrDefault();

            return quotation;
        }

        public void SaveSalesInvoice(SalesInvoiceHeader salesInvoice, SalesOrderHeader salesOrder)
        {
            // This method should be in a single transaction. when one fails, roll back all changes.
            try
            {
                // is there any new order line item? save it first. otherwise, saving invoice will fail.
                if (salesOrder != null && salesOrder.SalesOrderLines.Where(id => id.Id == 0).Count() > 0)
                {
                    if (salesOrder.Id == 0)
                    {
                        salesOrder.No = GetNextNumber(SequenceNumberTypes.SalesOrder).ToString();
                        _salesOrderRepo.Insert(salesOrder);
                    }
                    else
                    {
                        _salesOrderRepo.Update(salesOrder);
                    }
                }

                if (salesInvoice.Id == 0)
                {
                    salesInvoice.No = GetNextNumber(SequenceNumberTypes.SalesInvoice).ToString();
                    _salesInvoiceRepo.Insert(salesInvoice);
                }
                else
                {
                    _salesInvoiceRepo.Update(salesInvoice);
                }

                UpdateSalesOrderStatus(salesInvoice, salesOrder);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        private void UpdateSalesOrderStatus(SalesInvoiceHeader salesInvoice, SalesOrderHeader salesOrder)
        {
            // update the sales order status
            if (salesOrder == null)
            {
                // get the first order line
                salesOrder =
                    GetSalesOrderLineById(salesInvoice.SalesInvoiceLines.FirstOrDefault().SalesOrderLineId.GetValueOrDefault())
                        .SalesOrderHeader;
            }
            // if all orderline has no remaining qty to invoice, set the status to fullyinvoice
            bool hasRemainingQtyToInvoice = false;
            foreach (var line in salesOrder.SalesOrderLines)
            {
                if (line.GetRemainingQtyToInvoice() > 0)
                {
                    hasRemainingQtyToInvoice = true;
                    break;
                }
            }
            if (!hasRemainingQtyToInvoice)
            {
                salesOrder.Status = SalesOrderStatus.FullyInvoiced;
                _salesOrderRepo.Update(salesOrder);
            }
        }

        public void PostSalesInvoice(int invoiceId)
        {
            var salesInvoice = GetSalesInvoiceById(invoiceId);

            if (salesInvoice.GeneralLedgerHeaderId.HasValue)
                throw new Exception("Invoice is already posted. Update is not allowed.");

            decimal totalAmount = 0, totalDiscount = 0;

            var taxes = new List<KeyValuePair<int, decimal>>();
            var sales = new List<KeyValuePair<int, decimal>>();

            var glHeader = _financialService.CreateGeneralLedgerHeader(DocumentTypes.SalesInvoice, salesInvoice.Date, string.Empty);
            var customer = GetCustomerById(salesInvoice.CustomerId);

            foreach (var lineItem in salesInvoice.SalesInvoiceLines)
            {
                var item = _inventoryService.GetItemById(lineItem.ItemId);

                if (!item.GLAccountsValidated())
                    throw new Exception("Item is not correctly setup for financial transaction. Please check if GL accounts are all set.");

                var lineAmount = lineItem.Quantity * lineItem.Amount;

                var lineDiscountAmount = (lineItem.Discount / 100) * lineAmount;
                totalDiscount += lineDiscountAmount;

                var totalLineAmount = lineAmount - lineDiscountAmount;

                totalAmount += totalLineAmount;

                var lineTaxes = _financialService.ComputeOutputTax(salesInvoice.CustomerId, item.Id, lineItem.Quantity, lineItem.Amount, lineItem.Discount);

                foreach (var t in lineTaxes)
                    taxes.Add(t);

                var lineTaxAmount = lineTaxes != null && lineTaxes.Count > 0 ? lineTaxes.Sum(t => t.Value) : 0;
                totalLineAmount = totalLineAmount - lineTaxAmount;

                sales.Add(new KeyValuePair<int, decimal>(item.SalesAccountId.Value, totalLineAmount));

                if (item.ItemCategory.ItemType == ItemTypes.Purchased)
                {
                    lineItem.InventoryControlJournal = _inventoryService.CreateInventoryControlJournal(lineItem.ItemId,
                        lineItem.MeasurementId,
                        DocumentTypes.SalesInvoice,
                        null,
                        lineItem.Quantity,
                        lineItem.Quantity * item.Cost,
                        lineItem.Quantity * item.Price);
                }
            }

            totalAmount += salesInvoice.ShippingHandlingCharge;
            var debitCustomerAR = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, customer.AccountsReceivableAccount.Id, Math.Round(totalAmount, 2, MidpointRounding.ToEven));
            glHeader.GeneralLedgerLines.Add(debitCustomerAR);

            var groupedSalesAccount = from s in sales
                                      group s by s.Key into grouped
                                      select new
                                      {
                                          Key = grouped.Key,
                                          Value = grouped.Sum(s => s.Value)
                                      };

            foreach (var salesAccount in groupedSalesAccount)
            {
                var salesAmount = salesAccount.Value;
                var creditSalesAccount = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, salesAccount.Key, Math.Round(salesAmount, 2, MidpointRounding.ToEven));
                glHeader.GeneralLedgerLines.Add(creditSalesAccount);
            }

            if (taxes != null && taxes.Count > 0)
            {
                var groupedTaxes = from t in taxes
                                   group t by t.Key into grouped
                                   select new
                                   {
                                       Key = grouped.Key,
                                       Value = grouped.Sum(t => t.Value)
                                   };

                foreach (var tax in groupedTaxes)
                {
                    var tx = _financialService.GetTaxes().Where(t => t.Id == tax.Key).FirstOrDefault();
                    var creditSalesTaxAccount = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, tx.SalesAccountId.Value, Math.Round(tax.Value, 2, MidpointRounding.ToEven));
                    glHeader.GeneralLedgerLines.Add(creditSalesTaxAccount);
                }
            }

            if (totalDiscount > 0)
            {
                var salesDiscountAccount = base.GetGeneralLedgerSetting().SalesDiscountAccount;
                var creditSalesDiscountAccount = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, salesDiscountAccount.Id, Math.Round(totalDiscount, 2, MidpointRounding.ToEven));
                glHeader.GeneralLedgerLines.Add(creditSalesDiscountAccount);
            }

            if (salesInvoice.ShippingHandlingCharge > 0)
            {
                var shippingHandlingAccount = base.GetGeneralLedgerSetting().ShippingChargeAccount;
                var creditShippingHandlingAccount = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, shippingHandlingAccount.Id, Math.Round(salesInvoice.ShippingHandlingCharge, 2, MidpointRounding.ToEven));
                glHeader.GeneralLedgerLines.Add(creditShippingHandlingAccount);
            }

            if (_financialService.ValidateGeneralLedgerEntry(glHeader))
            {
                salesInvoice.No = GetNextNumber(SequenceNumberTypes.SalesInvoice).ToString();
                salesInvoice.GeneralLedgerHeader = glHeader;

                // Debit = COGS, Credit = Inventory
                var debitAccounts = new List<KeyValuePair<int, decimal>>();
                var creditAccounts = new List<KeyValuePair<int, decimal>>();

                foreach (var line in salesInvoice.SalesInvoiceLines)
                {
                    var item = _inventoryService.GetItemById(line.ItemId);
                    if (item.ItemCategory.ItemType == ItemTypes.Purchased)
                    {
                        debitAccounts.Add(new KeyValuePair<int, decimal>(item.CostOfGoodsSoldAccountId.Value, item.Cost.Value * line.Quantity));
                        creditAccounts.Add(new KeyValuePair<int, decimal>(item.InventoryAccountId.Value, item.Cost.Value * line.Quantity));
                    }
                }

                var groupedDebitAccounts = (from kvp in debitAccounts
                                            group kvp by kvp.Key into g
                                            select new KeyValuePair<int, decimal>(g.Key, g.Sum(e => e.Value)));
                var groupedCreditAccounts = (from kvp in creditAccounts
                                             group kvp by kvp.Key into g
                                             select new KeyValuePair<int, decimal>(g.Key, g.Sum(e => e.Value)));

                foreach (var account in groupedDebitAccounts)
                {
                    glHeader.GeneralLedgerLines.Add(_financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, account.Key, account.Value));
                }

                foreach (var account in groupedCreditAccounts)
                {
                    glHeader.GeneralLedgerLines.Add(_financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, account.Key, account.Value));
                }

                if (_financialService.ValidateGeneralLedgerEntry(glHeader))
                {
                    _salesInvoiceRepo.Update(salesInvoice);
                }
            }
        }
        
        public Contact GetContacyById(int id)
        {
 
            var contact = _contactRepo.GetAllIncluding(q => q.Party)
                .Where(q => q.Id == id)
                .FirstOrDefault();
            return contact;
        }

        public CustomerContact GetCustomerContact(int id)
        {
           return _customerContactRepo.GetById(id);


        }

        public void BookQuotation(int id)
        {
            var quoatation = _salesQuoteRepo.GetById(id);

            quoatation.Status = SalesQuoteStatus.Open;
            _salesQuoteRepo.Update(quoatation);
            
        }
 
    }
}
