//-----------------------------------------------------------------------
// <copyright file="PurchasingService.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.TaxSystem;
using Services.Financial;
using Services.Inventory;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Services.Sales;
using Core.Entitlements;

namespace Services.Purchasing
{
    public partial class PurchasingService : BaseService, IPurchasingService
    {
        private readonly IEntitlementService _entitlements;
        private readonly IFinancialService _financialService;
        private readonly IInventoryService _inventoryService;
        private readonly IStockService _stockService;

        private readonly ILogger<PurchasingService> _logger;

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
            IStockService stockService,
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
            IPurchaseOrderRepository purchaseOrderHeaderRepository,
            ILogger<PurchasingService> logger,
            IEntitlementService entitlements = null
            )
            : base(sequenceNumberRepo, generalLedgerSettingRepo, paymentTermRepo, bankRepo)
        {
            _entitlements = entitlements;
            _financialService = financialService;
            _inventoryService = inventoryService;
            _stockService = stockService;
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
            _logger = logger;
        }

        /// <summary>Adds a bill and approves it straight away; the seed data's bills are made this way.</summary>
        public void AddPurchaseInvoice(PurchaseInvoiceHeader purchaseIvoice, int? purchaseOrderId)
        {
            SaveBill(purchaseIvoice);
            ApproveBill(purchaseIvoice.Id);
        }

        public void AddPurchaseOrder(PurchaseOrderHeader purchaseOrder, bool toSave)
        {
            purchaseOrder.No = GetNextNumber(SequenceNumberTypes.PurchaseOrder).ToString();

            if (toSave)
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
                    item.PurchaseMeasurementId.HasValue ? item.PurchaseMeasurementId.Value : 1,
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

            GeneralLedgerHeader glHeader = _financialService.CreateGeneralLedgerHeader(DocumentTypes.PurchaseReceipt, purchaseOrderReceipt.Date, string.Empty, purchaseOrderReceipt.Id);
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

            foreach (var vendor in vendors)
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
                v => v.BillToContact,
                v => v.BillToContact.Party,
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

            if (vendor == null)
                return null;

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
            var query = _purchaseInvoiceRepo.GetAllIncluding(inv => inv.Vendor,
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

        /// <summary>
        /// Saves a draft bill. Each line gets its account (the default when none was picked) and its tax, worked out from
        /// the line's tax rate and stored, so the bill keeps what it was entered with.
        /// </summary>
        public void SaveBill(PurchaseInvoiceHeader bill, bool preserveTaxAmounts = false)
        {
            bill.EnsureEditable();
            if (bill.Id == 0)
                _entitlements?.EnsureCanCreate(MeteredResource.Bills);

            var vendor = bill.VendorId.HasValue ? _vendorRepo.GetById(bill.VendorId.Value) : null;
            var taxes = _financialService.GetTaxes().ToList();
            foreach (var line in bill.PurchaseInvoiceLines)
            {
                var tax = line.TaxId.HasValue
                    ? taxes.SingleOrDefault(t => t.Id == line.TaxId.Value)
                        ?? throw new InvalidOperationException("The selected tax was not found in this workspace.")
                    : null;
                if (tax != null && !tax.IsActive)
                    throw new InvalidOperationException($"{tax.TaxName} is archived. Pick another tax for this line.");

                var accountId = StockInventoryAccountId(line.ItemId)
                    ?? line.AccountId
                    ?? DefaultLineAccountId(line.ItemId, vendor);

                var item = line.ItemId > 0 ? _inventoryService.GetItemById(line.ItemId) : null;
                if (item?.ItemCategory?.ItemType == ItemTypes.Purchased && accountId == item.InventoryAccountId)
                {
                    if (line.Amount < 0)
                        throw new InvalidOperationException($"Stock item '{item.Description ?? item.Code ?? item.No}' cannot have a negative unit cost.");
                }

                line.AccountId = accountId;
                line.Cost = line.Amount;
                if (!preserveTaxAmounts)
                    line.TaxAmount = PurchaseInvoiceLine.TaxOf(line.Subtotal, tax?.Rate);
            }

            bill.VendorInvoiceNo ??= "";
            bill.Status ??= PurchaseInvoiceStatus.Draft;
            if (bill.Id == 0)
            {
                _purchaseInvoiceRepo.Insert(bill);
                _entitlements?.Record(MeteredResource.Bills, bill.Id);
            }
            else
                _purchaseInvoiceRepo.Update(bill);
        }

        /// <summary>
        /// The account a line is booked to when none was picked: an inventory item's inventory account, or else the
        /// vendor's purchase account (Purchase A/C).
        /// </summary>
        public int DefaultLineAccountId(int itemId, Vendor vendor)
        {
            var item = itemId > 0 ? _itemRepo.GetById(itemId) : null;
            if (item?.InventoryAccountId != null && item.ItemCategoryId.HasValue
                && _inventoryService.GetItemCategories().Any(c => c.Id == item.ItemCategoryId && c.ItemType == ItemTypes.Purchased))
                return item.InventoryAccountId.Value;

            return vendor?.PurchaseAccountId
                ?? _accountRepo.Table.Where(a => a.AccountCode == "50200").Select(a => (int?)a.Id).FirstOrDefault()
                ?? throw new InvalidOperationException("Pick an account for each line: this workspace has no purchase account.");
        }

        private int? StockInventoryAccountId(int itemId)
        {
            var item = itemId > 0 ? _inventoryService.GetItemById(itemId) : null;
            return item?.ItemCategory?.ItemType == ItemTypes.Purchased
                ? item.InventoryAccountId
                : null;
        }

        /// <summary>
        /// Approves a bill: posts it (each line's account and the claimable tax debited, Accounts Payable credited with
        /// the total), gives it the workspace's next bill number and records the vendor as it is now.
        /// </summary>
        public void ApproveBill(int billId, string? approvedBy = null, DateTime? approvedAt = null)
        {
            var bill = GetPurchaseInvoiceById(billId)
                ?? throw new InvalidOperationException($"Bill #{billId} was not found.");
            bill.EnsureCanBeApproved();

            var vendor = GetVendorById(bill.VendorId!.Value);
            if (vendor.ArchivedAt != null)
                throw new InvalidOperationException($"Vendor '{vendor.Party?.Name ?? vendor.Id.ToString()}' is archived. Restore it before approving bills from it.");

            var payableAccount = (vendor.AccountsPayableAccountId.HasValue
                ? _accountRepo.GetById(vendor.AccountsPayableAccountId.Value)
                : _accountRepo.Table.FirstOrDefault(a => a.AccountCode == "20110"))
                ?? throw new InvalidOperationException("This workspace has no Accounts Payable account.");
            if (payableAccount.ArchivedAt.HasValue)
                throw new InvalidOperationException($"Accounts Payable account {payableAccount.AccountCode} is archived.");

            var payableAccountId = payableAccount.Id;
            bill.PayableAccountId = payableAccountId;

            var taxes = _financialService.GetTaxes().ToList();

            var debits = new Dictionary<int, decimal>();
            void Debit(int accountId, decimal amount) => debits[accountId] = debits.GetValueOrDefault(accountId) + amount;

            var vendorName = vendor.Party?.Name ?? "vendor";
            bill.No ??= GetNextNumber(SequenceNumberTypes.PurchaseInvoice).ToString();
            foreach (var line in bill.PurchaseInvoiceLines)
            {
                var accountId = StockInventoryAccountId(line.ItemId)
                    ?? line.AccountId
                    ?? DefaultLineAccountId(line.ItemId, vendor);
                var lineAccount = _accountRepo.GetById(accountId);
                if (lineAccount == null || lineAccount.ArchivedAt.HasValue)
                    throw new InvalidOperationException($"Account {(lineAccount != null ? lineAccount.AccountCode : accountId.ToString())} is archived or not found.");

                line.AccountId = accountId;
                Debit(accountId, line.Subtotal);

                // Tax with no purchase tax account can't be claimed back, so it is part of what the line cost.
                Tax? tax = null;
                if (line.TaxId.HasValue)
                {
                    tax = taxes.FirstOrDefault(t => t.Id == line.TaxId);
                    if (tax == null || !tax.IsActive)
                        throw new InvalidOperationException($"Tax {(tax != null ? tax.TaxName : line.TaxId.ToString())} is archived or not found.");
                }

                var taxAccountId = tax?.PurchasingAccountId;
                if (line.TaxAmount != 0)
                    Debit(taxAccountId ?? accountId, line.TaxAmount);

                var item = _inventoryService.GetItemById(line.ItemId);
                if (item?.ItemCategory?.ItemType == ItemTypes.Purchased && accountId == item.InventoryAccountId)
                {
                    if (line.Amount < 0)
                        throw new InvalidOperationException($"Stock item '{item.Description ?? item.Code ?? item.No}' cannot have a negative unit cost.");

                    if (line.Quantity > 0)
                    {
                        var nonClaimableTax = taxAccountId.HasValue ? 0m : line.TaxAmount;
                        var unitCost = (line.Subtotal + nonClaimableTax) / line.Quantity;
                        line.InventoryControlJournal = _stockService.Receive(item, line.Quantity, unitCost,
                            DocumentTypes.PurchaseInvoice, bill.Id, bill.Date, $"Bill {bill.No} from {vendorName}");
                    }
                    else if (line.Quantity < 0)
                    {
                        line.InventoryControlJournal = _stockService.Issue(item, Math.Abs(line.Quantity),
                            DocumentTypes.PurchaseInvoice, bill.Id, bill.Date, $"Return to {vendorName} on bill {bill.No}");
                    }
                }
            }

            var description = string.IsNullOrWhiteSpace(bill.VendorInvoiceNo) ? $"Bill from {vendorName}" : $"Bill {bill.VendorInvoiceNo} from {vendorName}";
            var glHeader = _financialService.CreateGeneralLedgerHeader(DocumentTypes.PurchaseInvoice, bill.Date, description, bill.Id);
            foreach (var debit in debits.Where(d => d.Value != 0))
            {
                // A line that lowers the bill, such as a credit or a refund on it, is a credit to its account.
                glHeader.GeneralLedgerLines.Add(_financialService.CreateGeneralLedgerLine(
                    debit.Value > 0 ? DrOrCrSide.Dr : DrOrCrSide.Cr, debit.Key, Math.Abs(debit.Value)));
            }
            glHeader.GeneralLedgerLines.Add(_financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, payableAccountId, bill.Total()));
            _financialService.ValidateGeneralLedgerEntry(glHeader);

            bill.GeneralLedgerHeader = glHeader;
            bill.Status = PurchaseInvoiceStatus.Open;
            bill.ApprovedBy = approvedBy;
            bill.ApprovedAt = approvedAt ?? DateTime.UtcNow;
            bill.CaptureBillFromIfApproved(vendor.Party, vendor.BillToContact);
            _purchaseInvoiceRepo.Update(bill);
        }

        /// <summary>
        /// Voids a bill. An approved bill's posting is reversed, so it no longer counts as owed or as spent; a bill
        /// with payments can't be voided.
        /// </summary>
        public void VoidBill(int billId, DateTime voidDate, string? voidReason = null, string? voidedBy = null)
        {
            if (voidDate == default)
                throw new ArgumentException("Void date is required.");

            var bill = GetPurchaseInvoiceById(billId)
                ?? throw new InvalidOperationException($"Bill #{billId} was not found.");
            if (bill.IsVoid)
                return;

            bill.EnsureCanBeVoided();

            if (string.IsNullOrWhiteSpace(voidReason))
                throw new InvalidOperationException("A reason is required to void a bill.");

            if (voidDate.Date < bill.Date.Date)
                throw new InvalidOperationException($"The void date ({voidDate:yyyy-MM-dd}) cannot be earlier than the bill date ({bill.Date:yyyy-MM-dd}).");

            // Stock sold pre-check before posting any reversals: group by item so multiple lines sum together
            var stockGroups = bill.PurchaseInvoiceLines
                .Where(l => l.InventoryControlJournalId.HasValue)
                .GroupBy(l => l.ItemId);

            foreach (var group in stockGroups)
            {
                var item = _inventoryService.GetItemById(group.Key);
                if (item == null)
                    continue;

                decimal totalQtyReceived = 0;
                foreach (var line in group)
                {
                    var original = item.InventoryControlJournals.FirstOrDefault(j => j.Id == line.InventoryControlJournalId!.Value);
                    if (original != null && original.INQty.GetValueOrDefault() > 0)
                        totalQtyReceived += original.INQty.Value;
                }

                if (totalQtyReceived > 0 && item.QuantityOnHand < totalQtyReceived)
                {
                    var sold = StockService.RoundQuantity(totalQtyReceived - item.QuantityOnHand);
                    throw new InvalidOperationException($"{sold} of {item.Description ?? item.Code ?? item.No} have already been sold. Record a return to the vendor instead.");
                }
            }

            if (bill.GeneralLedgerHeaderId.HasValue && !bill.VoidGeneralLedgerHeaderId.HasValue)
                bill.VoidGeneralLedgerHeader = _financialService.CreateReversingEntry(bill.GeneralLedgerHeaderId.Value, $"Void of bill {bill.No ?? bill.Id.ToString()}", voidDate.Date);

            foreach (var line in bill.PurchaseInvoiceLines.Where(l => l.InventoryControlJournalId.HasValue))
            {
                var item = _inventoryService.GetItemById(line.ItemId);
                var original = item?.InventoryControlJournals.FirstOrDefault(j => j.Id == line.InventoryControlJournalId.Value);
                if (original == null)
                    continue;

                _stockService.Reverse(original, voidDate.Date,
                    $"Void of bill {bill.No ?? bill.Id.ToString()}");
            }
            bill.Status = PurchaseInvoiceStatus.Void;
            bill.VoidedAt = DateTime.UtcNow;
            bill.VoidedBy = voidedBy;
            bill.VoidReason = voidReason;
            _purchaseInvoiceRepo.Update(bill);
        }

        /// <summary>
        /// Records a payment on an approved bill from a cash or bank account: Accounts Payable debited, the account
        /// credited. A payment can't be more than what is still owed on the bill.
        /// </summary>
        public VendorPayment PayBill(int billId, int accountId, decimal amount, DateTime date,
            string? paymentMethod = null, string? referenceNo = null, string? memo = null)
        {
            if (date == default)
                throw new ArgumentException("Payment date is required.");

            var bill = GetPurchaseInvoiceById(billId)
                ?? throw new InvalidOperationException($"Bill #{billId} was not found.");

            bill.EnsureCanRecordPayment(amount);

            if (date.Date < bill.Date.Date)
                throw new InvalidOperationException($"The payment date ({date:yyyy-MM-dd}) cannot be earlier than the bill date ({bill.Date:yyyy-MM-dd}).");

            var vendor = GetVendorById(bill.VendorId!.Value);
            var payableAccountId = bill.PayableAccountId
                ?? throw new InvalidOperationException("This bill has no recorded Accounts Payable account.");

            var glHeader = _financialService.CreateGeneralLedgerHeader(DocumentTypes.PurchaseInvoicePayment, date.Date,
                $"Payment to {vendor.Party?.Name ?? "vendor"}" + (string.IsNullOrWhiteSpace(bill.VendorInvoiceNo) ? "" : $" for bill {bill.VendorInvoiceNo}"), bill.Id);
            glHeader.GeneralLedgerLines.Add(_financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, payableAccountId, amount));
            glHeader.GeneralLedgerLines.Add(_financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, accountId, amount));
            _financialService.ValidateGeneralLedgerEntry(glHeader);

            var payment = new VendorPayment
            {
                VendorId = vendor.Id,
                PurchaseInvoiceHeaderId = bill.Id,
                Date = date.Date,
                Amount = amount,
                AccountId = accountId,
                PaymentMethod = paymentMethod,
                ReferenceNo = referenceNo,
                Memo = memo,
                GeneralLedgerHeader = glHeader,
                No = GetNextNumber(SequenceNumberTypes.VendorPayment).ToString(),
            };
            _vendorPaymentRepo.Insert(payment);

            bill.VendorPayments.Add(payment);
            bill.Status = bill.BalanceDue() <= 0.005m ? PurchaseInvoiceStatus.Paid : PurchaseInvoiceStatus.Open;
            _purchaseInvoiceRepo.Update(bill);
            return payment;
        }

        /// <summary>
        /// Voids a payment on a bill: reverses its general-ledger posting and reopens the bill if it was fully paid.
        /// </summary>
        public VendorPayment VoidPayment(int paymentId, DateTime voidDate, string? voidReason = null, string? voidedBy = null)
        {
            if (voidDate == default)
                throw new ArgumentException("Void date is required.");
            if (string.IsNullOrWhiteSpace(voidReason))
                throw new InvalidOperationException("A reason is required to void a payment.");

            var payment = _vendorPaymentRepo.GetAllIncluding(p => p.PurchaseInvoiceHeader)
                .FirstOrDefault(p => p.Id == paymentId)
                ?? throw new InvalidOperationException($"Payment #{paymentId} was not found.");

            EnsurePaymentNotReconciled(payment);

            if (payment.IsVoid)
                return payment;

            if (voidDate.Date < payment.Date.Date)
                throw new InvalidOperationException($"The void date ({voidDate:yyyy-MM-dd}) cannot be earlier than the payment date ({payment.Date:yyyy-MM-dd}).");

            if (payment.GeneralLedgerHeaderId.HasValue && !payment.VoidGeneralLedgerHeaderId.HasValue)
            {
                payment.VoidGeneralLedgerHeader = _financialService.CreateReversingEntry(
                    payment.GeneralLedgerHeaderId.Value,
                    $"Void of payment {payment.No ?? payment.Id.ToString()}",
                    voidDate.Date);
            }

            payment.VoidedAt = DateTime.UtcNow;
            payment.VoidedBy = voidedBy;
            payment.VoidReason = voidReason;
            _vendorPaymentRepo.Update(payment);

            var bill = payment.PurchaseInvoiceHeader ?? (payment.PurchaseInvoiceHeaderId.HasValue ? GetPurchaseInvoiceById(payment.PurchaseInvoiceHeaderId.Value) : null);
            if (bill != null)
            {
                if (bill.Status == PurchaseInvoiceStatus.Paid && bill.BalanceDue() > 0)
                {
                    bill.Status = PurchaseInvoiceStatus.Open;
                }
                _purchaseInvoiceRepo.Update(bill);
            }

            return payment;
        }

        private static void EnsurePaymentNotReconciled(VendorPayment payment)
        {
            // Banking PR 2B hook: refuse voiding payments already cleared or reconciled in a bank reconciliation.
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
