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

namespace Services.Sales
{
    public partial class SalesService : BaseService, ISalesService
    {
        private readonly IFinancialService _financialService;
        private readonly IInventoryService _inventoryService;

        private readonly IRepository<SalesOrderHeader> _salesOrderRepo;
        private readonly IRepository<SalesInvoiceHeader> _salesInvoiceRepo;
        private readonly IRepository<SalesReceiptHeader> _salesReceiptRepo;
        private readonly IRepository<Customer> _customerRepo;
        private readonly IRepository<Account> _accountRepo;
        private readonly IRepository<Item> _itemRepo;
        private readonly IRepository<Measurement> _measurementRepo;
        private readonly IRepository<SequenceNumber> _sequenceNumberRepo;
        private readonly IRepository<PaymentTerm> _paymentTermRepo;
        private readonly IRepository<SalesDeliveryHeader> _salesDeliveryRepo;
        private readonly IRepository<Bank> _bankRepo;
        private readonly IRepository<GeneralLedgerSetting> _genetalLedgerSetting;
        private readonly IRepository<Contact> _contactRepo;

        public SalesService(IFinancialService financialService,
            IInventoryService inventoryService,
            IRepository<SalesOrderHeader> salesOrderRepo,
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
            IRepository<Contact> contactRepo)
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
        }

        public void AddSalesOrder(SalesOrderHeader salesOrder, bool toSave)
        {
            if (string.IsNullOrEmpty(salesOrder.No))
                salesOrder.No = GetNextNumber(SequenceNumberTypes.SalesOrder).ToString();
            if(toSave)
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

        public void AddSalesInvoice(SalesInvoiceHeader salesInvoice, int? salesDeliveryId)
        {   
            decimal taxAmount = 0, totalAmount = 0, totalDiscount = 0;

            var taxes = new List<KeyValuePair<int, decimal>>();
            var sales = new List<KeyValuePair<int, decimal>>();
            
            foreach (var lineItem in salesInvoice.SalesInvoiceLines)
            {
                var item = _itemRepo.GetById(lineItem.ItemId);

                var lineDiscountAmount = (lineItem.Discount / 100) * lineItem.Amount;
                totalDiscount += lineDiscountAmount;

                var lineAmount = lineItem.Amount - lineDiscountAmount;
                
                totalAmount += lineAmount;
                
                var lineTaxes = _financialService.ComputeOutputTax(salesInvoice.CustomerId, item.Id, lineItem.Quantity, lineItem.Amount, lineItem.Discount);
                
                foreach (var t in lineTaxes)
                    taxes.Add(t);
                
                sales.Add(new KeyValuePair<int, decimal>(item.SalesAccountId.Value, lineAmount));

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

            var glHeader = _financialService.CreateGeneralLedgerHeader(DocumentTypes.SalesInvoice, salesInvoice.Date, string.Empty);
            var customer = _customerRepo.GetById(salesInvoice.CustomerId);
            totalAmount += salesInvoice.ShippingHandlingCharge;
            var debitCustomerAR = _financialService.CreateGeneralLedgerLine(TransactionTypes.Dr, customer.AccountsReceivableAccount.Id, Math.Round(totalAmount, 2, MidpointRounding.ToEven));
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
                var salesAmount = (salesAccount.Value + totalDiscount) - taxAmount;
                var creditSalesAccount = _financialService.CreateGeneralLedgerLine(TransactionTypes.Cr, salesAccount.Key, Math.Round(salesAmount, 2, MidpointRounding.ToEven));
                glHeader.GeneralLedgerLines.Add(creditSalesAccount);
            }

            if (taxAmount > 0)
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
                    var creditSalesTaxAccount = _financialService.CreateGeneralLedgerLine(TransactionTypes.Cr, tx.SalesAccountId.Value, Math.Round(tax.Value, 2, MidpointRounding.ToEven));
                    glHeader.GeneralLedgerLines.Add(creditSalesTaxAccount);
                }
            }

            if (totalDiscount > 0)
            {
                var salesDiscountAccount = base.GetGeneralLedgerSetting().SalesDiscountAccount;
                var creditSalesDiscountAccount = _financialService.CreateGeneralLedgerLine(TransactionTypes.Dr, salesDiscountAccount.Id, Math.Round(totalDiscount, 2, MidpointRounding.ToEven));
                glHeader.GeneralLedgerLines.Add(creditSalesDiscountAccount);
            }

            if (salesInvoice.ShippingHandlingCharge > 0)
            {
                var shippingHandlingAccount = base.GetGeneralLedgerSetting().ShippingChargeAccount;
                var creditShippingHandlingAccount = _financialService.CreateGeneralLedgerLine(TransactionTypes.Cr, shippingHandlingAccount.Id, Math.Round(salesInvoice.ShippingHandlingCharge, 2, MidpointRounding.ToEven));
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
                        CreatedBy = salesInvoice.CreatedBy,
                        CreatedOn = DateTime.Now,
                        ModifiedBy = salesInvoice.ModifiedBy,
                        ModifiedOn = DateTime.Now,
                    };
                    foreach(var line in salesInvoice.SalesInvoiceLines)
                    {
                        var item = _itemRepo.GetById(line.ItemId);
                        salesDelivery.SalesDeliveryLines.Add(new SalesDeliveryLine()
                        {
                            ItemId = line.ItemId,
                            MeasurementId = line.MeasurementId,
                            Quantity = line.Quantity,
                            Discount = line.Discount,
                            Price = item.Cost.Value,
                            CreatedBy = salesInvoice.CreatedBy,
                            CreatedOn = DateTime.Now,
                            ModifiedBy = salesInvoice.ModifiedBy,
                            ModifiedOn = DateTime.Now,
                        });
                    }
                    AddSalesDelivery(salesDelivery, false);
                    salesInvoice.SalesDeliveryHeader = salesDelivery;
                }
                _salesInvoiceRepo.Insert(salesInvoice);
            }
        }

        public void AddSalesReceipt(SalesReceiptHeader salesReceipt)
        {
            var customer = _customerRepo.GetById(salesReceipt.CustomerId);
            var glHeader = _financialService.CreateGeneralLedgerHeader(DocumentTypes.SalesReceipt, salesReceipt.Date, string.Empty);
            var debit = _financialService.CreateGeneralLedgerLine(TransactionTypes.Dr, salesReceipt.AccountToDebitId.Value, salesReceipt.SalesReceiptLines.Sum(i => i.AmountPaid));
            var credit = _financialService.CreateGeneralLedgerLine(TransactionTypes.Cr, customer.AccountsReceivableAccountId.Value, salesReceipt.SalesReceiptLines.Sum(i => i.AmountPaid));
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
            var debit = _financialService.CreateGeneralLedgerLine(TransactionTypes.Dr, salesReceipt.AccountToDebitId.Value, salesReceipt.Amount);
            glHeader.GeneralLedgerLines.Add(debit);

            foreach (var line in salesReceipt.SalesReceiptLines)
            {
                var credit = _financialService.CreateGeneralLedgerLine(TransactionTypes.Cr, line.AccountToCreditId.Value, line.AmountPaid);
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
            var query = from invoice in _salesInvoiceRepo.Table
                        select invoice;
            return query.ToList();
        }

        public SalesInvoiceHeader GetSalesInvoiceById(int id)
        {
            return _salesInvoiceRepo.GetById(id);
        }

        public SalesInvoiceHeader GetSalesInvoiceByNo(string no)
        {
            var query = from invoice in _salesInvoiceRepo.Table
                        where invoice.No == no
                        select invoice;
            return query.FirstOrDefault();
        }

        public void UpdateSalesInvoice(SalesInvoiceHeader salesInvoice)
        {
            _salesInvoiceRepo.Update(salesInvoice);
        }

        public IEnumerable<SalesReceiptHeader> GetSalesReceipts()
        {
            var query = from receipt in _salesReceiptRepo.Table
                        select receipt;
            return query.ToList();
        }

        public SalesReceiptHeader GetSalesReceiptById(int id)
        {
            return _salesReceiptRepo.GetById(id);
        }

        public void UpdateSalesReceipt(SalesReceiptHeader salesReceipt)
        {
            _salesReceiptRepo.Update(salesReceipt);
        }

        public IEnumerable<Customer> GetCustomers()
        {
            var query = from c in _customerRepo.Table
                        select c;

            int count = query.Count();

            return query.AsEnumerable();
        }

        public Customer GetCustomerById(int id)
        {
            //var customer = _customerRepo.Table.Where(c => c.Id == id).FirstOrDefault();
            var customer = _customerRepo.GetById(id);
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
            var invoice = _salesInvoiceRepo.GetById(allocation.SalesInvoiceHeaderId);
            var receipt = _salesReceiptRepo.GetById(allocation.SalesReceiptHeaderId);

            var glHeader = _financialService.CreateGeneralLedgerHeader(Core.Domain.DocumentTypes.CustomerAllocation, allocation.Date, string.Empty);

            foreach (var line in receipt.SalesReceiptLines)
            {
                Account accountToDebit = invoice.Customer.CustomerAdvancesAccount;
                var debit = _financialService.CreateGeneralLedgerLine(Core.Domain.TransactionTypes.Dr, accountToDebit.Id, allocation.Amount);
                glHeader.GeneralLedgerLines.Add(debit);
            }

            Account accountToCredit = invoice.Customer.SalesAccount;
            var credit = _financialService.CreateGeneralLedgerLine(Core.Domain.TransactionTypes.Cr, accountToCredit.Id, allocation.Amount);
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
            _customerRepo.Insert(customer);
        }

        public new IEnumerable<PaymentTerm> GetPaymentTerms()
        {
            return base.GetPaymentTerms();
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
                glHeader.GeneralLedgerLines.Add(_financialService.CreateGeneralLedgerLine(TransactionTypes.Dr, account.Key, account.Value));
            }
            foreach (var account in groupedCreditAccounts)
            {
                glHeader.GeneralLedgerLines.Add(_financialService.CreateGeneralLedgerLine(TransactionTypes.Cr, account.Key, account.Value));
            }

            if (_financialService.ValidateGeneralLedgerEntry(glHeader))
            {
                salesDelivery.GeneralLedgerHeader = glHeader;

                salesDelivery.No = GetNextNumber(SequenceNumberTypes.SalesDelivery).ToString();

                if(!salesDelivery.SalesOrderHeaderId.HasValue)
                {
                    var salesOrder = new SalesOrderHeader()
                    {
                        CustomerId = salesDelivery.CustomerId,
                        PaymentTermId = salesDelivery.PaymentTermId,
                        Date = salesDelivery.Date,
                        No = GetNextNumber(SequenceNumberTypes.SalesOrder).ToString(),
                        CreatedBy = salesDelivery.CreatedBy,
                        CreatedOn = DateTime.Now,
                        ModifiedBy = salesDelivery.ModifiedBy,
                        ModifiedOn = DateTime.Now,
                    };

                    foreach(var line in salesDelivery.SalesDeliveryLines)
                    {
                        var item = _inventoryService.GetItemById(line.ItemId.Value);
                        salesOrder.SalesOrderLines.Add(new SalesOrderLine()
                        {
                            ItemId = item.Id,
                            MeasurementId = line.MeasurementId.Value,
                            Quantity = line.Quantity,
                            Amount = item.Price.Value,
                            CreatedBy = salesDelivery.CreatedBy,
                            CreatedOn = DateTime.Now,
                            ModifiedBy = salesDelivery.ModifiedBy,
                            ModifiedOn = DateTime.Now,
                        });
                    }
                    AddSalesOrder(salesOrder, false);
                    salesDelivery.SalesOrderHeader = salesOrder;
                }

                if (toSave)
                    _salesDeliveryRepo.Insert(salesDelivery);
            }
        }

        public IEnumerable<SalesOrderHeader> GetSalesOrders()
        {
            var query = from f in _salesOrderRepo.Table
                        select f;
            return query;
        }

        public SalesOrderHeader GetSalesOrderById(int id)
        {
            return _salesOrderRepo.GetById(id);
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

        public int SaveContact(Contact contact)
        {
            _contactRepo.Insert(contact);
            return contact.Id;
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
    }
}
