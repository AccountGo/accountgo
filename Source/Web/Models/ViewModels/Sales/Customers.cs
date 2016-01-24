//-----------------------------------------------------------------------
// <copyright file="Customers.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;

namespace Web.Models.ViewModels.Sales
{
    public class Customers
    {
        public Customers()
        {
            CustomerListLines = new HashSet<CustomerListLine>();
        }

        public ICollection<CustomerListLine> CustomerListLines { get; set; }
    }

    public class CustomerListLine
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public decimal Balance { get; set; }
    }

    public class CustomerDetail
    {
        public CustomerDetail()
        {
            CustomerAllocations = new HashSet<CustomerAllocation>();
            CustomerInvoices = new HashSet<CustomerSalesInvoice>();
            ActualAllocations = new HashSet<Allocations>();
        }

        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Balance { get; set; }

        public virtual ICollection<CustomerAllocation> CustomerAllocations { get; set; }
        public virtual ICollection<CustomerSalesInvoice> CustomerInvoices { get; set; }
        public ICollection<Allocations> ActualAllocations { get; set; }
    }

    public class CustomerReceipt
    {

    }

    public class CustomerSalesInvoice
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string InvoiceNo { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; }
    }

    public class CustomerSalesOrder
    {

    }

    public class CustomerAllocation
    {
        public int Id { get; set; }
        public decimal AmountAllocated { get; set; }
        public decimal AvailableAmountToAllocate { get; set; }
    }

    public class Allocations
    {
        public string InvoiceNo { get; set; }
        public string ReceiptNo { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
    }

    public class Allocate
    {
        public Allocate()
        {
            OpenInvoices = new HashSet<SelectListItem>();
        }

        public int ReceiptId { get; set; }
        public int InvoiceId { get; set; }
        public string InvoiceNo { get; set; }
        public decimal AmountToAllocate { get; set; }
        public decimal InvoiceAmount { get; set; }
        public decimal TotalAmountAvailableToAllocate { get; set; }
        public decimal LeftToAllocateFromReceipt { get; set; }

        public ICollection<SelectListItem> OpenInvoices { get; set; }
    }

    public class EditCustomer
    {
        public EditCustomer()
        {
        }

        public int Id { get; set; }
        [Required]
        public string Name { get; set; }
        public int? PrimaryContactId { get; set; }

        public int? AccountsReceivableAccountId { get; set; }
        public int? SalesAccountId { get; set; }
        public int? SalesDiscountAccountId { get; set; }
        public int? PromptPaymentDiscountAccountId { get; set; }
        public int? CustomerAdvancesAccountId { get; set; }
    }

    public class AddCustomer
    {
        public string Name { get; set; }
    }
}
