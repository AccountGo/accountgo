//-----------------------------------------------------------------------
// <copyright file="EnumTypes.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

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

    public enum JournalVoucherTypes
    {
        CashDisbursement = 1
    }

    public enum PurchaseStatuses
    {
        Open,
        PartiallyReceived,
        FullReceived,
        Invoiced,
        Closed
    }

    public enum PurchaseInvoiceStatuses
    {
        Open,
        Paid
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
        //Draft,
        Open,
        Overdue,
        Closed,
        Void
    }
}
