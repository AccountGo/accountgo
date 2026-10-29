//-----------------------------------------------------------------------
// <copyright file="EnumTypes.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;

namespace Core.Domain
{
    public enum AccountClasses
    {
        Assets = 1,
        Liabilities = 2,
        Equity = 3,
        Revenue = 4,
        Expense = 5,
        Temporary = 6
    }

    public enum DocumentTypes
    {
        SalesQuote = 1,
        SalesOrder,
        SalesDelivery,
        SalesInvoice,
        SalesReceipt,
        SalesDebitMemo,
        SalesCreditMemo,
        PurchaseOrder,
        PurchaseReceipt,
        PurchaseInvoice,
        PurchaseDebitMemo,
        PurchaseCreditMemo,
        PurchaseInvoicePayment,
        JournalEntry,
        CustomerAllocation,
        InventoryAdjustment,
        /// <summary>Bank register document (Phase 1: opening balance; Phase 2: money in/out, transfer, etc.).</summary>
        BankTransaction = 17
    }

    public enum AccountTypes
    {
        Posting = 1,
        Heading,
        Total,
        BeginTotal,
        EndTotal
    }

    public enum DrOrCrSide
    {
        NA = 0,
        Dr = 1,
        Cr = 2
    }

    public enum PartyTypes
    {
        Customer = 1,
        Vendor = 2,
        Contact = 3
    }

    /// <summary>
    /// Journal voucher is prepared for the transactions which does not relate to sales, purchases, cash, bank, material returns
    /// </summary>
    public enum JournalVoucherTypes
    {
        OpeningBalances = 1,
        ClosingEntries = 2,
        AdjustmentEntries = 3,
        CorrectionEntries = 4,
        TransferEntries = 5,
        Other = 6,
    }

    public enum PurchaseOrderStatus
    {
        Draft = 0,
        Open = 1,
        PartiallyReceived = 2,
        FullReceived = 3,
        Invoiced = 4,
        Closed = 5
    }

    public enum PurchaseInvoiceStatus
    {
        Draft = 0,
        Open = 1,
        Paid = 2,
        Overdue = 3,
        Void = 4
    }

    public enum SequenceNumberTypes
    {
        SalesQuote = 1,
        SalesOrder,
        SalesDelivery,
        SalesInvoice,
        SalesReceipt,
        PurchaseOrder,
        PurchaseReceipt,
        PurchaseInvoice,
        VendorPayment,
        JournalEntry,
        Item,
        Customer,
        Vendor,
        Contact,
        DonationInvoice,
        InventoryAdjustment,
        BankTransaction
    }

    /// <summary>Kinds for <see cref="Financials.BankTransaction"/> (D6).</summary>
    public enum BankTransactionKinds
    {
        MoneyIn = 1,
        MoneyOut = 2,
        Transfer = 3,
        Fee = 4,
        Interest = 5,
        Adjustment = 6,
        OpeningBalance = 7
    }

    public enum AddressTypes
    {
        Office,
        Home
    }

    public enum ContactTypes
    {
        Customer = 1,
        Vendor = 2,
        Company = 3
    }

    public enum ItemTypes
    {
        Manufactured = 1,
        Purchased,
        Service,
        Charge
    }

    public enum PaymentTypes
    {
        Prepaymnet = 1,
        Cash,
        AfterNoOfDays,
        DayInTheFollowingMonth
    }

    public enum PaymentMethods
    {
        Check = 1,
        ETransfer,
        Cash,
        CreditCard,
        Other
    }

    public enum BankTypes
    {
        CheckingAccount = 1,
        SavingsAccount = 2,
        CashAccount = 3,
        /// <summary>Reserved for P2; not offered in the UI yet.</summary>
        CreditCard = 4,
        /// <summary>Reserved for P2; not offered in the UI yet.</summary>
        LineOfCredit = 5
    }

    public enum SalesInvoiceStatus
    {
        Draft,
        Open = 1,
        Overdue,
        Closed,
        Void
    }

    public enum SalesOrderStatus
    {
        Draft = 0,
        Open = 1,
        Overdue = 2,
        Closed = 3,
        Void = 4,
        PartiallyInvoiced = 5,
        FullyInvoiced = 6
    }

    public enum SalesQuoteStatus
    {
        Draft = 0,
        Open = 1,
        Overdue = 2,
        Closed = 3,
        Void = 4,
        [Display(Name = "Closed - Order Created")]
        ClosedOrderCreated = 5
    }

    public enum AccountingBasis
    {
        Accrual = 1,
        Cash = 2
    }

    public enum ReportingFramework
    {
        IfrsForSmes = 1,
        UsGaapPrivate = 2,
        Ifrs = 3,
        CashBasis = 4
    }

    public enum TaxRegime
    {
        None = 0,
        SalesTax = 1,
        VatGst = 2
    }

    public enum FiscalYearPattern
    {
        Calendar = 1,
        AprMar = 2,
        JulJun = 3,
        Custom = 4
    }

    public enum IndustryTemplate
    {
        Services = 1,
        ProductRetail = 2,
        Mixed = 3
    }

    public enum InventoryCostingMethod
    {
        None = 0,
        Fifo = 1,
        WeightedAverage = 2
    }
}
