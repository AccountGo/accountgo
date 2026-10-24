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
using Microsoft.Extensions.Logging;
using AutoMapper;
using Core.Domain.Error;
using System.Threading.Tasks;
using System.Globalization;

namespace Services.Sales
{
    public partial class SalesService : BaseService, ISalesService
    {
        private readonly IFinancialService _financialService;
        private readonly IInventoryService _inventoryService;
        private readonly IStockService _stockService;

        private readonly IMapper _mapper;
        private readonly ILogger<SalesService> _logger;

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
            IStockService stockService,
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
            IRepository<VendorContact> vendorContactRepo,
            ILogger<SalesService> logger,
            IMapper mapper)
            : base(sequenceNumberRepo, generalLedgerSetting, paymentTermRepo, bankRepo)
        {
            _financialService = financialService;
            _inventoryService = inventoryService;
            _stockService = stockService;
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
            _logger = logger;
            _mapper = mapper;
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

            var glHeader = _financialService.CreateGeneralLedgerHeader(DocumentTypes.SalesInvoice, salesInvoice.Date, string.Empty, salesInvoice.Id);
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

                int salesAccountId = item.SalesAccountId ?? item.SalesAccount?.Id ?? 
                    _financialService.GetAccounts().FirstOrDefault(a => a.AccountCode == "40100")?.Id ?? 
                    _financialService.GetAccounts().FirstOrDefault(a => a.AccountClassId == 4)?.Id ?? 1;
                sales.Add(new KeyValuePair<int, decimal>(salesAccountId, totalLineAmount));

                if (item.ItemCategory != null && item.ItemCategory.ItemType == ItemTypes.Purchased)
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
            int arAccountId = customer.AccountsReceivableAccountId ?? customer.AccountsReceivableAccount?.Id ??
                _financialService.GetAccounts().FirstOrDefault(a => a.AccountCode == "10120")?.Id ??
                _financialService.GetAccounts().FirstOrDefault(a => a.AccountClassId == 1)?.Id ?? 1;
            var debitCustomerAR = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, arAccountId, Math.Round(totalAmount, 2, MidpointRounding.ToEven));
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
                var salesAmount = Math.Round(salesAccount.Value, 2, MidpointRounding.ToEven);
                if (salesAmount == 0)
                    continue;
                var creditSalesAccount = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, salesAccount.Key, salesAmount);
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
                    var taxAmount = Math.Round(tax.Value, 2, MidpointRounding.ToEven);
                    if (taxAmount == 0)
                        continue;
                    var tx = _financialService.GetTaxes().Where(t => t.Id == tax.Key).FirstOrDefault();
                    var creditSalesTaxAccount = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, tx.SalesAccountId.Value, taxAmount);
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

                salesInvoice.No = AllocateInvoiceNumber(salesInvoice.Id);

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
            var glHeader = _financialService.CreateGeneralLedgerHeader(DocumentTypes.SalesReceipt, salesReceipt.Date, string.Empty, salesReceipt.Id);
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
            var glHeader = _financialService.CreateGeneralLedgerHeader(DocumentTypes.SalesReceipt, salesReceipt.Date, string.Empty, salesReceipt.Id);
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
                inv => inv.SalesInvoiceLines,
                inv => inv.CustomerAllocations);

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

            if (invoice != null)
            {
                foreach (var line in invoice.SalesInvoiceLines)
                {
                    line.Item = _itemRepo.GetById(line.ItemId);
                    line.Measurement = _measurementRepo.GetById(line.MeasurementId);
                }
            }

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
            ApplyInvoiceNumber(salesInvoice);
            _salesInvoiceRepo.Update(salesInvoice);
        }

        public string PeekNextInvoiceNumber()
        {
            var next = PeekNextNumber(SequenceNumberTypes.SalesInvoice);
            while (InvoiceNumberTaken(next.ToString(CultureInfo.InvariantCulture), 0))
                next++;
            return next.ToString(CultureInfo.InvariantCulture);
        }

        // Every invoice write goes through here. Callers that issue invoices hold a write transaction (see
        // SalesController), so the read-then-advance of the sequence can't interleave; the unique index on
        // (CompanyId, No) is the last line of defence, locally and in D1.
        private void ApplyInvoiceNumber(SalesInvoiceHeader invoice)
        {
            invoice.No = InvoiceNumber.Normalize(invoice.No);
            if (invoice.No != null && InvoiceNumberTaken(invoice.No, invoice.Id))
                throw new InvoiceNumberException(InvoiceNumberError.Taken,
                    $"Invoice #{invoice.No} already exists. Use another number, or leave it blank to number it automatically.");

            if (!InvoiceNumber.IsRequired(invoice.Status))
                return;

            if (invoice.No == null)
            {
                invoice.No = AllocateInvoiceNumber(invoice.Id);
            }
            else if (InvoiceNumber.AsSequence(invoice.No) is int typed)
            {
                // A typed plain number moves the sequence past it, so automatic numbering continues from there.
                AdvanceNextNumber(SequenceNumberTypes.SalesInvoice, typed + 1);
            }
        }

        private string AllocateInvoiceNumber(int invoiceId)
        {
            // Skip numbers a draft already holds because they were typed in.
            var next = PeekNextNumber(SequenceNumberTypes.SalesInvoice);
            while (InvoiceNumberTaken(next.ToString(CultureInfo.InvariantCulture), invoiceId))
                next++;
            AdvanceNextNumber(SequenceNumberTypes.SalesInvoice, next + 1);
            return next.ToString(CultureInfo.InvariantCulture);
        }

        private bool InvoiceNumberTaken(string number, int exceptInvoiceId)
        {
            var lower = number.ToLower();
            return _salesInvoiceRepo.Table.Any(i => i.Id != exceptInvoiceId && i.No != null && i.No.ToLower() == lower);
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

            if (customer == null)
                return null;

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
            if (allocation == null)
                throw new ArgumentNullException(nameof(allocation));
            if (allocation.Amount <= 0m)
                throw new InvalidOperationException("Allocation amount must be more than zero.");

            var invoice = GetSalesInvoiceById(allocation.SalesInvoiceHeaderId)
                ?? throw new InvalidOperationException($"Invoice #{allocation.SalesInvoiceHeaderId} was not found.");
            var receipt = GetSalesReceiptById(allocation.SalesReceiptHeaderId)
                ?? throw new InvalidOperationException($"Receipt #{allocation.SalesReceiptHeaderId} was not found.");

            var receiptTotal = receipt.SalesReceiptLines != null && receipt.SalesReceiptLines.Any()
                ? receipt.SalesReceiptLines.Sum(a => a.AmountPaid)
                : receipt.Amount;
            var alreadyAllocatedOnReceipt = receipt.CustomerAllocations != null
                ? receipt.CustomerAllocations.Where(a => a.Id == 0 || a.Id != allocation.Id).Sum(a => a.Amount)
                : 0m;
            var availableOnReceipt = receiptTotal - alreadyAllocatedOnReceipt;
            if (allocation.Amount > availableOnReceipt)
                throw new InvalidOperationException($"Allocation amount ({allocation.Amount:0.00}) exceeds the unallocated receipt balance ({availableOnReceipt:0.00}).");

            var glHeader = _financialService.CreateGeneralLedgerHeader(Core.Domain.DocumentTypes.CustomerAllocation, allocation.Date, string.Empty);

            var debitAccountId = invoice.Customer?.CustomerAdvancesAccount?.Id
                ?? invoice.Customer?.CustomerAdvancesAccountId
                ?? receipt.SalesReceiptLines?.FirstOrDefault(l => l.AccountToCreditId.HasValue)?.AccountToCreditId
                ?? throw new InvalidOperationException("Customer has no customer advances account configured.");
            glHeader.GeneralLedgerLines.Add(_financialService.CreateGeneralLedgerLine(Core.Domain.DrOrCrSide.Dr, debitAccountId, allocation.Amount));

            var creditAccountId = invoice.Customer?.AccountsReceivableAccount?.Id
                ?? invoice.Customer?.AccountsReceivableAccountId
                ?? throw new InvalidOperationException("Customer has no accounts receivable account configured.");
            glHeader.GeneralLedgerLines.Add(_financialService.CreateGeneralLedgerLine(Core.Domain.DrOrCrSide.Cr, creditAccountId, allocation.Amount));

            if (_financialService.ValidateGeneralLedgerEntry(glHeader))
            {
                _financialService.SaveGeneralLedgerEntry(glHeader);
                allocation.GeneralLedgerHeaderId = glHeader.Id;
                invoice.CustomerAllocations.Add(allocation);
                _salesInvoiceRepo.Update(invoice);
                if (allocation.Id > 0)
                {
                    glHeader.SourceDocumentId = allocation.Id;
                    _financialService.UpdateGeneralLedgerHeader(glHeader);
                }
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
            var defaultTaxGroup = _taxGroupRepo.Table.FirstOrDefault(tg => tg.Description == "GST/HST" && tg.IsActive);
            if (!customer.TaxGroupId.HasValue && defaultTaxGroup != null)
                customer.TaxGroupId = defaultTaxGroup.Id;

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
            var glHeader = _financialService.CreateGeneralLedgerHeader(DocumentTypes.SalesDelivery, salesDelivery.Date, string.Empty, salesDelivery.Id);
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
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
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

            if (quotation != null)
            {
                foreach (var line in quotation.SalesQuoteLines)
                {
                    line.Item = _itemRepo.GetById(line.ItemId);
                    line.Measurement = _measurementRepo.GetById(line.MeasurementId);
                }
            }

            return quotation;
        }

        public Result<Dto.Sales.SalesInvoice> CreateSalesInvoice(Dto.Sales.SalesInvoice salesInvoiceDto)
        {
            Core.Domain.Sales.SalesOrderHeader salesOrder = null;
            Core.Domain.Sales.SalesInvoiceHeader salesInvoice = null;

            if (!salesInvoiceDto.FromSalesOrderId.HasValue)
            {
                salesOrder = _mapper.Map<Core.Domain.Sales.SalesOrderHeader>(salesInvoiceDto);
            }
            else
            {
                // your invoice is created from existing (open) sales order.
                salesOrder = GetSalesOrderById(salesInvoiceDto.FromSalesOrderId.GetValueOrDefault());

                if (salesOrder is null)
                {
                    var message = $"Sales order {salesInvoiceDto.FromSalesOrderId} in SalesInvoice not found.";
                    return Result<Dto.Sales.SalesInvoice>.Failure(Error.RecordNotFound(message));
                }
            }

            salesInvoice = _mapper.Map<Core.Domain.Sales.SalesInvoiceHeader>(salesInvoiceDto);
            salesInvoice.No = InvoiceNumber.ForSave(null, null, exists: false, salesInvoiceDto.No);

            foreach (var invoiceLine in salesInvoice.SalesInvoiceLines)
            {
                if (invoiceLine.SalesOrderLineId == 0)
                {
                    salesOrder.SalesOrderLines.Add(invoiceLine.SalesOrderLine);
                    invoiceLine.SalesOrderLineId = invoiceLine.SalesOrderLine.Id;
                }
            }

            _logger.LogInformation("SaveSalesInvoice API " + salesInvoice.CustomerId);

            SaveSalesInvoice(salesInvoice, salesOrder);

            salesInvoiceDto.Id = salesInvoice.Id;
            return Result<Dto.Sales.SalesInvoice>.Success(salesInvoiceDto);
        }

        public Result<Dto.Sales.SalesInvoice> UpdateSalesInvoice(Dto.Sales.SalesInvoice salesInvoiceDto)
        {
            Core.Domain.Sales.SalesInvoiceHeader salesInvoice = null;
            Core.Domain.Sales.SalesOrderHeader salesOrder = null;

            salesInvoice = GetSalesInvoiceById(salesInvoiceDto.Id);

            if (salesInvoice is null)
            {
                var message = $"Sales invoice {salesInvoiceDto.Id} not found.";
                return Result<Dto.Sales.SalesInvoice>.Failure(Error.RecordNotFound(message));
            }

            if (salesInvoice.GeneralLedgerHeaderId.HasValue)
                throw new Exception("Invoice is already posted. Update is not allowed.");

            var currentNo = salesInvoice.No;
            var currentStatus = salesInvoice.Status;
            _mapper.Map<Dto.Sales.SalesInvoice, Core.Domain.Sales.SalesInvoiceHeader>(salesInvoiceDto, salesInvoice);
            salesInvoice.No = InvoiceNumber.ForSave(currentNo, currentStatus, exists: true, salesInvoiceDto.No);

            int existingOrderLineId = salesInvoice.SalesInvoiceLines.FirstOrDefault().SalesOrderLineId ??= 0;
            foreach (var invoiceLine in salesInvoice.SalesInvoiceLines!)
            {
                salesOrder = GetSalesOrderLineById(invoiceLine!.SalesOrderLineId ??= 0).SalesOrderHeader;

                if (invoiceLine.Id == 0)
                {
                    // use the last value of existingLine
                    salesOrder = GetSalesOrderLineById(existingOrderLineId).SalesOrderHeader;
                    salesOrder.SalesOrderLines.Add(invoiceLine.SalesOrderLine);
                }
                else
                {
                    // TODO: Udpate Existing SalesOrderLine with SalesInvoiceLine.
                    invoiceLine.SalesOrderLine = GetSalesOrderLineById(invoiceLine.SalesOrderLineId.GetValueOrDefault());
                }
            }

            var deleted = (from line in salesInvoice.SalesInvoiceLines
                           where !salesInvoiceDto.SalesInvoiceLines.Any(x => x.Id == line.Id)
                           select line).ToList();

            foreach (var line in deleted)
            {
                salesInvoice.SalesInvoiceLines.Remove(line);
            }

            _logger.LogInformation("SaveSalesInvoice API " + salesInvoice.CustomerId);

            SaveSalesInvoice(salesInvoice, salesOrder);

            return Result<Dto.Sales.SalesInvoice>.Success(null);
        }

        public async Task<Result<Dto.Sales.SalesInvoice>> DeleteSalesInvoiceAsync(int id)
        {
            var salesInvoice = GetSalesInvoiceById(id);

            if (salesInvoice is null)
            {
                var message = $"Sales invoice {id} not found.";
                return Result<Dto.Sales.SalesInvoice>.Failure(Error.RecordNotFound(message));
            }

            // An issued invoice keeps its number for good, so the series has no gaps; it can be voided, not deleted.
            if (salesInvoice.Status != SalesInvoiceStatus.Draft
                || salesInvoice.GeneralLedgerHeaderId.HasValue
                || (salesInvoice.CustomerAllocations?.Any() ?? false))
            {
                return Result<Dto.Sales.SalesInvoice>.Failure(Error.ValidationError(
                    "Only draft invoices can be deleted. Void an issued invoice instead."));
            }

            await _salesInvoiceRepo.DeleteAsync(salesInvoice);

            return Result<Dto.Sales.SalesInvoice>.Success(null);
        }

        public void SaveSalesInvoice(SalesInvoiceHeader salesInvoice, SalesOrderHeader salesOrder)
        {
            if (salesInvoice.GeneralLedgerHeaderId.HasValue)
                throw new InvalidOperationException("Invoice is already posted. Update is not allowed.");

            var taxes = _financialService.GetTaxes().ToList();
            foreach (var line in salesInvoice.SalesInvoiceLines)
            {
                var tax = line.TaxId.HasValue
                    ? taxes.SingleOrDefault(t => t.Id == line.TaxId.Value)
                        ?? throw new InvalidOperationException("The selected tax was not found in this workspace.")
                    : null;
                if (tax != null && !tax.IsActive)
                    throw new InvalidOperationException($"{tax.TaxName} is archived. Pick another tax for this line.");
                line.TaxAmount = Core.Domain.Purchases.PurchaseInvoiceLine.TaxOf(line.Subtotal, tax?.Rate);
            }

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

                ApplyInvoiceNumber(salesInvoice);
                if (salesInvoice.Id == 0)
                {
                    _salesInvoiceRepo.Insert(salesInvoice);
                }
                else
                {
                    _logger.LogInformation("Invoice " + salesInvoice.CustomerId + " is being updated.");
                    _salesInvoiceRepo.Update(salesInvoice);
                }

                UpdateSalesOrderStatus(salesInvoice, salesOrder);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                throw; // Re-throw to let controller handle the error
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

            var taxes = new List<KeyValuePair<int, decimal>>();
            var sales = new List<KeyValuePair<int, decimal>>();
            var availableTaxes = _financialService.GetTaxes().ToList();
            // Discounts post to their own account so sales report before them; without one, sales post net.
            var salesDiscountAccountId = base.GetGeneralLedgerSetting()?.SalesDiscountAccountId;
            decimal totalDiscount = 0;
            var projectedItems = new Dictionary<int, Item>();
            var stockIssues = new List<(SalesInvoiceLine Line, Item Item, InventoryControlJournal Movement)>();

            var glHeader = _financialService.CreateGeneralLedgerHeader(DocumentTypes.SalesInvoice, salesInvoice.Date, string.Empty, salesInvoice.Id);
            var customer = GetCustomerById(salesInvoice.CustomerId);

            foreach (var lineItem in salesInvoice.SalesInvoiceLines)
            {
                var item = _inventoryService.GetItemById(lineItem.ItemId);

                if (!item.GLAccountsValidated())
                    throw new Exception("Item is not correctly setup for financial transaction. Please check if GL accounts are all set.");

                var totalLineAmount = lineItem.Subtotal;
                if (salesDiscountAccountId.HasValue)
                {
                    var grossLineAmount = Math.Round(lineItem.Quantity * lineItem.Amount, 2, MidpointRounding.AwayFromZero);
                    totalDiscount += grossLineAmount - totalLineAmount;
                    totalLineAmount = grossLineAmount;
                }
                if (lineItem.TaxId.HasValue)
                {
                    var tax = availableTaxes.SingleOrDefault(t => t.Id == lineItem.TaxId.Value)
                        ?? throw new InvalidOperationException("The selected tax was not found in this workspace.");
                    if (!tax.SalesAccountId.HasValue)
                        throw new InvalidOperationException($"Tax {tax.TaxName} has no sales tax account");
                    if (lineItem.TaxAmount != 0)
                        taxes.Add(new KeyValuePair<int, decimal>(tax.SalesAccountId.Value, lineItem.TaxAmount));
                }

                int salesAccountId = item.SalesAccountId ?? item.SalesAccount?.Id ?? 
                    _financialService.GetAccounts().FirstOrDefault(a => a.AccountCode == "40100")?.Id ?? 
                    _financialService.GetAccounts().FirstOrDefault(a => a.AccountClassId == 4)?.Id ?? 1;
                sales.Add(new KeyValuePair<int, decimal>(salesAccountId, totalLineAmount));

                if (item.ItemCategory != null && item.ItemCategory.ItemType == ItemTypes.Purchased && lineItem.Quantity > 0)
                {
                    InventoryControlJournal movement;
                    if (lineItem.InventoryControlJournalId.HasValue)
                    {
                        movement = item.InventoryControlJournals.Single(j => j.Id == lineItem.InventoryControlJournalId.Value);
                    }
                    else
                    {
                        if (!projectedItems.TryGetValue(item.Id, out var projected))
                        {
                            projected = new Item
                            {
                                Id = item.Id,
                                QuantityOnHand = item.QuantityOnHand,
                                AverageCost = item.AverageCost,
                                StockValue = item.StockValue,
                                PurchaseMeasurementId = item.PurchaseMeasurementId,
                                SmallestMeasurementId = item.SmallestMeasurementId,
                                SellMeasurementId = item.SellMeasurementId
                            };
                            projectedItems.Add(item.Id, projected);
                        }
                        // Cost repeated lines against a detached projection; no tracked stock changes before GL validation.
                        movement = _stockService.Issue(projected, lineItem.Quantity,
                            DocumentTypes.SalesInvoice, salesInvoice.Id, salesInvoice.Date,
                            $"Invoice {salesInvoice.No ?? salesInvoice.Id.ToString()}");
                        stockIssues.Add((lineItem, item, movement));
                    }
                    var totalCost = movement.TotalCost.GetValueOrDefault();
                    if (totalCost != 0)
                    {
                        glHeader.GeneralLedgerLines.Add(_financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, item.CostOfGoodsSoldAccountId.Value, totalCost));
                        glHeader.GeneralLedgerLines.Add(_financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, item.InventoryAccountId.Value, totalCost));
                    }
                }
            }

            int arAccountId = customer.AccountsReceivableAccountId ?? customer.AccountsReceivableAccount?.Id ??
                _financialService.GetAccounts().FirstOrDefault(a => a.AccountCode == "10120")?.Id ??
                _financialService.GetAccounts().FirstOrDefault(a => a.AccountClassId == 1)?.Id ?? 1;
            var debitCustomerAR = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, arAccountId, salesInvoice.GrandTotal());
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
                if (salesAmount == 0)
                    continue;
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
                    var creditSalesTaxAccount = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, tax.Key, tax.Value);
                    glHeader.GeneralLedgerLines.Add(creditSalesTaxAccount);
                }
            }

            if (totalDiscount != 0)
                glHeader.GeneralLedgerLines.Add(_financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, salesDiscountAccountId!.Value, totalDiscount));

            if (salesInvoice.ShippingHandlingCharge > 0)
            {
                var shippingAccountId = base.GetGeneralLedgerSetting().ShippingChargeAccountId
                    ?? throw new InvalidOperationException("Set up a shipping charge account before posting shipping.");
                var creditShippingHandlingAccount = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, shippingAccountId, Math.Round(salesInvoice.ShippingHandlingCharge, 2, MidpointRounding.AwayFromZero));
                glHeader.GeneralLedgerLines.Add(creditShippingHandlingAccount);
            }

            _financialService.ValidateGeneralLedgerEntry(glHeader);
            salesInvoice.No = InvoiceNumber.Normalize(salesInvoice.No) ?? AllocateInvoiceNumber(salesInvoice.Id);
            foreach (var issue in stockIssues)
            {
                var projected = projectedItems[issue.Item.Id];
                issue.Item.QuantityOnHand = projected.QuantityOnHand;
                issue.Item.AverageCost = projected.AverageCost;
                issue.Item.StockValue = projected.StockValue;
                issue.Movement.Item = issue.Item;
                issue.Item.InventoryControlJournals.Add(issue.Movement);
                issue.Line.InventoryControlJournal = issue.Movement;
            }
            salesInvoice.GeneralLedgerHeader = glHeader;
            salesInvoice.Status = Core.Domain.SalesInvoiceStatus.Open;
            _salesInvoiceRepo.Update(salesInvoice);
        }

        /// <summary>Restores stock for an invoice after its general-ledger posting has been reversed.</summary>
        public void VoidSalesInvoiceStock(SalesInvoiceHeader invoice)
        {
            if (invoice == null)
                throw new ArgumentNullException(nameof(invoice));
            if (invoice.Status == SalesInvoiceStatus.Void)
                return;

            var reversalDate = invoice.VoidGeneralLedgerHeader?.Date ?? invoice.Date;
            foreach (var line in invoice.SalesInvoiceLines.Where(l => l.InventoryControlJournalId.HasValue))
            {
                var item = _inventoryService.GetItemById(line.ItemId);
                var original = item?.InventoryControlJournals.FirstOrDefault(j => j.Id == line.InventoryControlJournalId.Value);
                if (original == null)
                    continue;

                _stockService.Reverse(original, reversalDate, $"Void of invoice {invoice.No ?? invoice.Id.ToString()}");
            }
        }

        /// <summary>Returns a non-blocking warning for each stock item the invoice will leave below zero.</summary>
        public List<string> GetSalesInvoiceStockWarnings(SalesInvoiceHeader invoice)
        {
            if (invoice == null)
                throw new ArgumentNullException(nameof(invoice));

            var warnings = new List<string>();
            foreach (var group in invoice.SalesInvoiceLines.GroupBy(line => line.ItemId))
            {
                var item = _inventoryService.GetItemById(group.Key);
                if (item?.ItemCategory?.ItemType != ItemTypes.Purchased)
                    continue;

                var quantity = group.Sum(line => line.Quantity);
                if (quantity > item.QuantityOnHand)
                {
                    warnings.Add($"Only {item.QuantityOnHand.ToString("0.####", CultureInfo.InvariantCulture)} of {item.Description} in stock.");
                }
            }
            return warnings;
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
