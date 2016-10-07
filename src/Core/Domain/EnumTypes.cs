//-----------------------------------------------------------------------
// <copyright file="EnumTypes.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
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
        CustomerAllocation
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
    }

    public enum PurchaseOrderStatus
    {
        Draft = 0,
        Open = 1,
        PartiallyReceived = 2,
        FullReceived = 3,
        Invoiced = 4 ,
        Closed = 5
    }

    public enum PurchaseInvoiceStatus
    {
        Draft = 0,
        Open = 1,
        Paid = 2
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
        Contact
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

    public enum BankTypes
    {
        CheckingAccount = 1,
        SavingsAccount,
        CashAccount
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
}
