//-----------------------------------------------------------------------
// <copyright file="PurchasingService.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Services.Financial;
using Services.Inventory;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading;

namespace Services.Purchasing
{
    public partial class PurchasingService : BaseService, IPurchasingService
    {
        private readonly IFinancialService _financialService;
        private readonly IInventoryService _inventoryService;

        private readonly IRepository<PurchaseOrderHeader> _purchaseOrderRepo;
        private readonly IRepository<PurchaseOrderLine> _purchaseOrderLineRepo;
        private readonly IRepository<PurchaseInvoiceHeader> _purchaseInvoiceRepo;
        private readonly IRepository<PurchaseReceiptHeader> _purchaseReceiptRepo;
        private readonly IRepository<Vendor> _vendorRepo;
        private readonly IRepository<Account> _accountRepo;
        private readonly IRepository<Item> _itemRepo;
        private readonly IRepository<Measurement> _measurementRepo;
        private readonly IRepository<SequenceNumber> _sequenceNumberRepo;
        private readonly IRepository<VendorPayment> _vendorPaymentRepo;
        private readonly IRepository<GeneralLedgerSetting> _generalLedgerSettingRepo;
        private readonly IRepository<PaymentTerm> _paymentTermRepo;
        private readonly IRepository<Bank> _bankRepo;
        private readonly IRepository<Contact> _contactRepo;
        private readonly IPurchaseOrderRepository _purchaseOrderHeaderRepository;

        public PurchasingService(IFinancialService financialService,
            IInventoryService inventoryService,
            IRepository<PurchaseOrderHeader> purchaseOrderRepo,
            IRepository<PurchaseOrderLine> purchaseOrderLineRepo,
            IRepository<PurchaseInvoiceHeader> purchaseInvoiceRepo,
            IRepository<PurchaseReceiptHeader> purchaseReceiptRepo,
            IRepository<Vendor> vendorRepo,
            IRepository<Account> accountRepo,
            IRepository<Item> itemRepo,
            IRepository<Measurement> measurementRepo,
            IRepository<SequenceNumber> sequenceNumberRepo,
            IRepository<VendorPayment> vendorPaymentRepo,
            IRepository<GeneralLedgerSetting> generalLedgerSettingRepo,
            IRepository<PaymentTerm> paymentTermRepo,
            IRepository<Bank> bankRepo,
            IRepository<Contact> contactRepo,
            IPurchaseOrderRepository purchaseOrderHeaderRepository
            )
            : base(sequenceNumberRepo, generalLedgerSettingRepo, paymentTermRepo, bankRepo)
        {
            _financialService = financialService;
            _inventoryService = inventoryService;
            _purchaseOrderRepo = purchaseOrderRepo;
            _purchaseOrderLineRepo = purchaseOrderLineRepo;
            _purchaseInvoiceRepo = purchaseInvoiceRepo;
            _purchaseReceiptRepo = purchaseReceiptRepo;
            _vendorRepo = vendorRepo;
            _accountRepo = accountRepo;
            _itemRepo = itemRepo;
            _measurementRepo = measurementRepo;
            _sequenceNumberRepo = sequenceNumberRepo;
            _vendorPaymentRepo = vendorPaymentRepo;
            _generalLedgerSettingRepo = generalLedgerSettingRepo;
            _paymentTermRepo = paymentTermRepo;
            _bankRepo = bankRepo;
            _purchaseOrderHeaderRepository = purchaseOrderHeaderRepository;
            _contactRepo = contactRepo;
        }

        public void AddPurchaseInvoice(PurchaseInvoiceHeader purchaseIvoice, int? purchaseOrderId)
        {
            #region Auto create purchase order
            if (!purchaseOrderId.HasValue)
            {
                var po = new PurchaseOrderHeader()
                {
                    Date = purchaseIvoice.Date,
                    No = GetNextNumber(SequenceNumberTypes.PurchaseOrder).ToString(),
                    Vendor = purchaseIvoice.Vendor,
                    VendorId = purchaseIvoice.VendorId.Value,
                    Description = purchaseIvoice.Description,
                };
                foreach (var line in purchaseIvoice.PurchaseInvoiceLines)
                {
                    var item = _inventoryService.GetItemById(line.ItemId);

                    po.PurchaseOrderLines.Add(new PurchaseOrderLine()
                    {
                        ItemId = item.Id,
                        MeasurementId = line.MeasurementId,
                        Quantity = line.Quantity,
                        Cost = item.Cost.Value,
                        Discount = line.Discount.HasValue ? line.Discount.Value : 0,
                        Amount = line.Amount * line.Quantity,
                    });
                }
                purchaseIvoice.PurchaseOrders.Add(po);
                //var poReceipt = new PurchaseReceiptHeader()
                //{
                //    Date = DateTime.Now,
                //    VendorId = po.VendorId.Value,
                //    PurchaseOrderHeaderId = po.Id,
                //};

                //foreach (var line in purchaseIvoice.PurchaseInvoiceLines)
                //{
                //    poReceipt.PurchaseReceiptLines.Add(new PurchaseReceiptLine()
                //    {
                //        ItemId = line.ItemId,
                //        MeasurementId = line.MeasurementId,
                //        Quantity = line.Quantity,
                //        ReceivedQuantity = (line.ReceivedQuantity.HasValue ? line.ReceivedQuantity.Value : 0),
                //        Cost = line.Cost.Value,
                //        Amount = line.Cost.Value * (line.ReceivedQuantity.HasValue ? line.ReceivedQuantity.Value : 0),
                //    });
                //}

                //po.PurchaseReceipts.Add(poReceipt);

                //AddPurchaseOrderReceipt(poReceipt);
            }
            #endregion

            var glHeader = _financialService.CreateGeneralLedgerHeader(DocumentTypes.PurchaseInvoice, purchaseIvoice.Date, purchaseIvoice.Description);

            decimal totalTaxAmount = 0, totalAmount = 0, totalDiscount = 0;
            var taxes = new List<KeyValuePair<int, decimal>>();

            foreach (var line in purchaseIvoice.PurchaseInvoiceLines)
            {
                var lineTaxes = _financialService.ComputeInputTax(purchaseIvoice.VendorId.GetValueOrDefault(), line.ItemId, line.Quantity, line.Amount, line.Discount.GetValueOrDefault());

                var lineAmount = line.Quantity * line.Amount;

                var totalLineAmount = lineAmount + lineTaxes.Sum(t => t.Value);

                totalAmount += (decimal)totalLineAmount;
                
                foreach (var t in lineTaxes)
                    taxes.Add(t);

                var item = _inventoryService.GetItemById(line.ItemId);
                decimal lineItemTotalAmountAfterTax = line.Amount - lineTaxes.Sum(t => t.Value);

                GeneralLedgerLine debitInventory = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, item.InventoryAccount.Id, lineItemTotalAmountAfterTax);
                glHeader.GeneralLedgerLines.Add(debitInventory);

                GeneralLedgerLine creditGRNClearingAccount = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, GetGeneralLedgerSetting().GoodsReceiptNoteClearingAccountId.Value, lineItemTotalAmountAfterTax);
                glHeader.GeneralLedgerLines.Add(creditGRNClearingAccount);

                line.InventoryControlJournal = _inventoryService.CreateInventoryControlJournal(line.ItemId,
                    line.MeasurementId,
                    DocumentTypes.PurchaseReceipt,
                    line.Quantity,
                    null,
                    line.Quantity * item.Cost,
                    null);
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

                totalTaxAmount = taxes.Sum(t => t.Value);

                foreach (var tax in groupedTaxes)
                {
                    var tx = _financialService.GetTaxes().Where(t => t.Id == tax.Key).FirstOrDefault();
                    var debitPurchaseTaxAccount = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, tx.PurchasingAccountId.Value, tax.Value);
                    glHeader.GeneralLedgerLines.Add(debitPurchaseTaxAccount);
                }
            }

            if (totalDiscount > 0)
            {

            }

            Vendor vendor = GetVendorById(purchaseIvoice.VendorId.Value);
            var creditVendorAccount = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, vendor.AccountsPayableAccountId.Value, totalAmount);
            glHeader.GeneralLedgerLines.Add(creditVendorAccount);

            var debitGRNClearingAccount = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, GetGeneralLedgerSetting().GoodsReceiptNoteClearingAccountId.Value, totalAmount - totalTaxAmount);
            glHeader.GeneralLedgerLines.Add(debitGRNClearingAccount);

            if (_financialService.ValidateGeneralLedgerEntry(glHeader))
            {
                purchaseIvoice.GeneralLedgerHeader = glHeader;

                purchaseIvoice.No = GetNextNumber(SequenceNumberTypes.PurchaseInvoice).ToString();
                _purchaseInvoiceRepo.Insert(purchaseIvoice);

                if (purchaseOrderId.HasValue)
                {
                    // TODO: Look for other way to update the purchase order's invoice header id field so that it shall be in a single transaction along with purchase invoice saving
                    var purchOrder = GetPurchaseOrderById(purchaseOrderId.GetValueOrDefault());
                    //purchOrder.PurchaseInvoiceHeaderId = purchaseIvoice.Id;
                    _purchaseOrderRepo.Update(purchOrder);
                }
            }
        }

        public void AddPurchaseOrder(PurchaseOrderHeader purchaseOrder, bool toSave)
        {
            purchaseOrder.No = GetNextNumber(SequenceNumberTypes.PurchaseOrder).ToString();
            
            if(toSave)
                _purchaseOrderRepo.Insert(purchaseOrder);
        }

        public void UpdatePurchaseOrder(PurchaseOrderHeader purchaseOrder)
        {
            _purchaseOrderRepo.Update(purchaseOrder);
        }

        public void AddPurchaseOrderReceipt(PurchaseReceiptHeader purchaseOrderReceipt)
        {
            var glLines = new List<GeneralLedgerLine>();
            decimal totalAmount = purchaseOrderReceipt.PurchaseReceiptLines.Sum(d => d.Amount);
            decimal taxAmount = purchaseOrderReceipt.GetTotalTax();
            decimal totalDiscount = 0;

            foreach (var lineItem in purchaseOrderReceipt.PurchaseReceiptLines)
            {
                var item = _inventoryService.GetItemById(lineItem.ItemId);
                decimal lineItemTotalAmountAfterTax = lineItem.Amount - lineItem.LineTaxAmount;

                GeneralLedgerLine debitInventory = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, item.InventoryAccount.Id, lineItemTotalAmountAfterTax);
                glLines.Add(debitInventory);

                GeneralLedgerLine creditGRNClearingAccount = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, GetGeneralLedgerSetting().GoodsReceiptNoteClearingAccountId.Value, lineItemTotalAmountAfterTax);
                glLines.Add(creditGRNClearingAccount);

                lineItem.InventoryControlJournal = _inventoryService.CreateInventoryControlJournal(lineItem.ItemId,
                    lineItem.MeasurementId,
                    DocumentTypes.PurchaseReceipt,
                    lineItem.ReceivedQuantity,
                    null,
                    lineItem.ReceivedQuantity * item.Cost,
                    null);
            }

            if (taxAmount > 0)
            {
            }

            if (totalDiscount > 0)
            {
            }

            GeneralLedgerHeader glHeader = _financialService.CreateGeneralLedgerHeader(DocumentTypes.PurchaseReceipt, purchaseOrderReceipt.Date, string.Empty);
            glHeader.GeneralLedgerLines = glLines;

            if (_financialService.ValidateGeneralLedgerEntry(glHeader))
            {
                purchaseOrderReceipt.GeneralLedgerHeader = glHeader;

                purchaseOrderReceipt.No = GetNextNumber(SequenceNumberTypes.PurchaseReceipt).ToString();
                _purchaseReceiptRepo.Insert(purchaseOrderReceipt);
            }
        }

        public IEnumerable<Vendor> GetVendors()
        {
            System.Linq.Expressions.Expression<Func<Vendor, object>>[] includeProperties = {
                v => v.Party,
                v => v.PrimaryContact,
                v => v.PrimaryContact.Party,
                v => v.PaymentTerm,
                v => v.PurchaseAccount,
                v => v.PurchaseDiscountAccount,
                v => v.AccountsPayableAccount,
                v => v.VendorPayments,
                v => v.TaxGroup,
                v => v.PurchaseInvoices,
                v => v.PurchaseOrders,
                v => v.PurchaseReceipts
            };

            var vendors = _vendorRepo.GetAllIncluding(includeProperties);

            foreach(var vendor in vendors)
            {
                foreach (var invoice in vendor.PurchaseInvoices)
                {
                    invoice.PurchaseInvoiceLines = GetPurchaseInvoiceById(invoice.Id).PurchaseInvoiceLines;
                }
            }

            return vendors;
        }

        public Vendor GetVendorById(int id)
        {
            var vendor = _vendorRepo.GetAllIncluding(
                v => v.Party,
                v => v.PrimaryContact,
                v => v.PrimaryContact.Party,                
                v => v.PaymentTerm,
                v => v.PurchaseAccount,
                v => v.PurchaseDiscountAccount,
                v => v.AccountsPayableAccount,
                v => v.VendorPayments,
                v => v.TaxGroup,
                v => v.PurchaseInvoices,
                v => v.PurchaseOrders,
                v => v.PurchaseReceipts,
                v => v.VendorContact
                )
                .Where(v => v.Id == id)
                .FirstOrDefault();

            foreach (var invoice in vendor.PurchaseInvoices)
            {
                invoice.PurchaseInvoiceLines = GetPurchaseInvoiceById(invoice.Id).PurchaseInvoiceLines;
            }

            foreach (var vendorContact in vendor.VendorContact)
            {
                var contact = GetContacyById(vendorContact.ContactId);
                vendorContact.Contact = contact;
            }

            return vendor;
        }

        public IEnumerable<PurchaseOrderHeader> GetPurchaseOrders()
        {
            var query = _purchaseOrderHeaderRepository.GetAllPurchaseOrders();
            return query;
        }

        public PurchaseOrderHeader GetPurchaseOrderById(int id)
        {
            var purchOrder = GetPurchaseOrders()
                .Where(po => po.Id == id)
                .FirstOrDefault();

            return purchOrder;
        }

        public PurchaseOrderLine GetPurchaseOrderLineById(int id)
        {
            var purchaseOrderLine = _purchaseOrderLineRepo.GetAllIncluding(
                line => line.PurhcaseOrderHeader,
                line => line.PurhcaseOrderHeader.PurchaseOrderLines
                )
                .Where(line => line.Id == id)
                .FirstOrDefault();

            return purchaseOrderLine;
        }

        public PurchaseReceiptHeader GetPurchaseReceiptById(int id)
        {
            var purchReceipt = _purchaseReceiptRepo.GetAllIncluding(pr => pr.Vendor,
                pr => pr.Vendor.Party,
                pr => pr.PurchaseReceiptLines)
                .Where(pr => pr.Id == id)
                .FirstOrDefault();

            return purchReceipt;
        }

        public void AddVendor(Vendor vendor)
        {
            vendor.AccountsPayableAccountId = _accountRepo.Table.Where(a => a.AccountCode == "20110").FirstOrDefault().Id;
            vendor.PurchaseAccountId = _accountRepo.Table.Where(a => a.AccountCode == "50200").FirstOrDefault().Id;
            vendor.PurchaseDiscountAccountId = _accountRepo.Table.Where(a => a.AccountCode == "50400").FirstOrDefault().Id;

            vendor.No = GetNextNumber(SequenceNumberTypes.Vendor).ToString();
            _vendorRepo.Insert(vendor);
        }

        public void UpdateVendor(Vendor vendor)
        {
            _vendorRepo.Update(vendor);
        }

        public IEnumerable<PurchaseInvoiceHeader> GetPurchaseInvoices()
        {
            var query =_purchaseInvoiceRepo.GetAllIncluding(inv => inv.Vendor,
                inv => inv.Vendor.Party,
                inv => inv.VendorPayments,
                inv => inv.PurchaseInvoiceLines,
                inv => inv.GeneralLedgerHeader,
                inv => inv.GeneralLedgerHeader.GeneralLedgerLines);

            return query.AsEnumerable();
        }

        public PurchaseInvoiceHeader GetPurchaseInvoiceById(int id)
        {
            var invoice = _purchaseInvoiceRepo.GetAllIncluding(inv => inv.Vendor,
                inv => inv.Vendor.Party,
                inv => inv.PurchaseInvoiceLines,
                inv => inv.VendorPayments)
                .Where(inv => inv.Id == id)
                .FirstOrDefault();

            return invoice;
        }

        public void SavePayment(int invoiceId, int vendorId, int accountId, decimal amount, DateTime date)
        {
            var payment = new VendorPayment()
            {
                VendorId = vendorId,
                PurchaseInvoiceHeaderId = invoiceId,
                Date = date,
                Amount = amount,
            };
            var vendor = GetVendorById(vendorId);
            var debit = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, vendor.AccountsPayableAccountId.Value, amount);
            var credit = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, accountId, amount);
            var glHeader = _financialService.CreateGeneralLedgerHeader(DocumentTypes.PurchaseInvoicePayment, date, string.Empty);
            glHeader.GeneralLedgerLines.Add(debit);
            glHeader.GeneralLedgerLines.Add(credit);

            if (_financialService.GetAccount(accountId).Balance < amount)
                throw new Exception("Not enough balance.");

            if(_financialService.ValidateGeneralLedgerEntry(glHeader))
            {
                payment.GeneralLedgerHeader = glHeader;
                payment.No = GetNextNumber(SequenceNumberTypes.VendorPayment).ToString();
                _vendorPaymentRepo.Insert(payment);
            }
        }

        public void SavePurchaseInvoice(PurchaseInvoiceHeader purchaseInvoice, PurchaseOrderHeader purchaseOrder)
        {
            // This method should be in a single transaction. when one fails, roll back all changes.
            try
            {
                // is there any new order line item? save it first. otherwise, saving invoice will fail.
                if (purchaseOrder != null && purchaseOrder.PurchaseOrderLines.Where(id => id.Id == 0).Count() > 0)
                {
                    if (purchaseOrder.Id == 0)
                    {
                        purchaseOrder.No = GetNextNumber(SequenceNumberTypes.SalesOrder).ToString();
                        purchaseOrder.Status = (int)PurchaseOrderStatus.Draft;
                        _purchaseOrderRepo.Insert(purchaseOrder);
                    }
                    else
                    {
                        _purchaseOrderRepo.Update(purchaseOrder);
                    }
                }

                if (purchaseInvoice.Id == 0)
                {
                    purchaseInvoice.No = GetNextNumber(SequenceNumberTypes.SalesInvoice).ToString();
                    _purchaseInvoiceRepo.Insert(purchaseInvoice);
                }
                else
                {
                    _purchaseInvoiceRepo.Update(purchaseInvoice);
                }

                UpdatePurchaseOrderStatus(purchaseInvoice, purchaseOrder);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        private void UpdatePurchaseOrderStatus(PurchaseInvoiceHeader purchaseInvoice, PurchaseOrderHeader purchaseOrder)
        {
            // update the purchase order status
            if (purchaseOrder == null)
            {
                // get the first order line
                purchaseOrder =
                    GetPurchaseOrderLineById(
                        purchaseInvoice.PurchaseInvoiceLines.FirstOrDefault().PurchaseOrderLineId.GetValueOrDefault())
                        .PurhcaseOrderHeader;
            }
            // if all orderline has no remaining qty to invoice, set the status to fullyinvoice
            bool hasRemainingQtyToInvoice = false;
            foreach (var line in purchaseOrder.PurchaseOrderLines)
            {
                if (line.GetRemainingQtyToInvoice() > 0)
                {
                    hasRemainingQtyToInvoice = true;
                    break;
                }
            }
            if (!hasRemainingQtyToInvoice)
            {
                purchaseOrder.Status = PurchaseOrderStatus.FullReceived;
                _purchaseOrderRepo.Update(purchaseOrder);
            }
        }

        public void PostPurchaseInvoice(int invoiceId)
        {
            var purchaseInvoice = GetPurchaseInvoiceById(invoiceId);

            if (purchaseInvoice.GeneralLedgerHeaderId.HasValue)
                throw new Exception("Invoice is already posted. Update is not allowed.");

            var glHeader = _financialService.CreateGeneralLedgerHeader(DocumentTypes.PurchaseInvoice, purchaseInvoice.Date, purchaseInvoice.Description);

            decimal totalTaxAmount = 0, totalAmount = 0, totalDiscount = 0;
            var taxes = new List<KeyValuePair<int, decimal>>();

            foreach (var lineItem in purchaseInvoice.PurchaseInvoiceLines)
            {
                var item = _inventoryService.GetItemById(lineItem.ItemId);

                if (!item.GLAccountsValidated())
                    throw new Exception("Item is not correctly setup for financial transaction. Please check if GL accounts are all set.");

                var lineAmount = lineItem.Quantity * lineItem.Amount;

                var lineDiscountAmount = (lineItem.Discount / 100) * lineAmount;
                totalDiscount += lineDiscountAmount.GetValueOrDefault();

                var totalLineAmount = lineAmount - lineDiscountAmount;

                totalAmount += totalLineAmount.GetValueOrDefault();

                var lineTaxes = _financialService.ComputeInputTax(purchaseInvoice.VendorId.GetValueOrDefault(), lineItem.ItemId, lineItem.Quantity, lineItem.Amount, lineItem.Discount.GetValueOrDefault());

                foreach (var t in lineTaxes)
                    taxes.Add(t);

                var lineTaxAmount = lineTaxes != null && lineTaxes.Count > 0 ? lineTaxes.Sum(t => t.Value) : 0;
                totalLineAmount = totalLineAmount - lineTaxAmount;

                GeneralLedgerLine debitInventory = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, item.InventoryAccount.Id, totalLineAmount.GetValueOrDefault());
                glHeader.GeneralLedgerLines.Add(debitInventory);

                GeneralLedgerLine creditGRNClearingAccount = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, GetGeneralLedgerSetting().GoodsReceiptNoteClearingAccountId.Value, totalLineAmount.GetValueOrDefault());
                glHeader.GeneralLedgerLines.Add(creditGRNClearingAccount);

                lineItem.InventoryControlJournal = _inventoryService.CreateInventoryControlJournal(lineItem.ItemId,
                    lineItem.MeasurementId,
                    DocumentTypes.PurchaseReceipt,
                    lineItem.Quantity,
                    null,
                    lineItem.Quantity * item.Cost,
                    null);
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

                totalTaxAmount = taxes.Sum(t => t.Value);

                foreach (var tax in groupedTaxes)
                {
                    var tx = _financialService.GetTaxes().Where(t => t.Id == tax.Key).FirstOrDefault();
                    var debitPurchaseTaxAccount = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, tx.PurchasingAccountId.Value, tax.Value);
                    glHeader.GeneralLedgerLines.Add(debitPurchaseTaxAccount);
                }
            }

            if (totalDiscount > 0)
            {
                var purchasesDiscountAccount = base.GetGeneralLedgerSetting().PurchaseDiscountAccount;
                var creditPurchaseDiscountAccount = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, purchasesDiscountAccount.Id, Math.Round(totalDiscount, 2, MidpointRounding.ToEven));
                glHeader.GeneralLedgerLines.Add(creditPurchaseDiscountAccount);
            }

            Vendor vendor = GetVendorById(purchaseInvoice.VendorId.Value);
            var creditVendorAccount = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, vendor.AccountsPayableAccountId.Value, totalAmount);
            glHeader.GeneralLedgerLines.Add(creditVendorAccount);

            var debitGRNClearingAccount = _financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, GetGeneralLedgerSetting().GoodsReceiptNoteClearingAccountId.Value, totalAmount - totalTaxAmount);
            glHeader.GeneralLedgerLines.Add(debitGRNClearingAccount);

            if (_financialService.ValidateGeneralLedgerEntry(glHeader))
            {
                purchaseInvoice.GeneralLedgerHeader = glHeader;

                purchaseInvoice.No = GetNextNumber(SequenceNumberTypes.PurchaseInvoice).ToString();
                _purchaseInvoiceRepo.Update(purchaseInvoice);
            }
        }

        public Contact GetContacyById(int id)
        {

            var contact = _contactRepo.GetAllIncluding(q => q.Party)
                .Where(q => q.Id == id)
                .FirstOrDefault();
            return contact;
        }

    }
}
