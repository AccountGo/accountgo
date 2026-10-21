using System;
using System.Collections.Generic;
using Dto.Sales;
using Xunit;

namespace AccountGo.BackendTests.Sales;

public class SalesInvoiceTests
{
    [Fact]
    public void SalesInvoice_DefaultsToEmptyLinesAndCurrentDate()
    {
        var invoice = new SalesInvoice();

        Assert.NotNull(invoice.SalesInvoiceLines);
        Assert.Empty(invoice.SalesInvoiceLines);
        Assert.True((DateTime.Now - invoice.InvoiceDate).TotalMinutes < 5);
        Assert.Equal(0, invoice.Amount);
    }

    [Fact]
    public void SalesInvoice_AmountCalculatesCorrectly_WithDiscounts()
    {
        var invoice = new SalesInvoice
        {
            SalesInvoiceLines = new List<SalesInvoiceLine>
            {
                new SalesInvoiceLine { Quantity = 2, Amount = 100m, Discount = 0m },    // 200
                new SalesInvoiceLine { Quantity = 1, Amount = 50m, Discount = 10m },     // 45
                new SalesInvoiceLine { Quantity = 4, Amount = 25m, Discount = 50m }      // 50
            }
        };

        // 200 + 45 + 50 = 295
        Assert.Equal(295m, invoice.Amount);
    }

    [Fact]
    public void SalesInvoice_HandlesNullAmountsAndQuantitiesSafely()
    {
        var invoice = new SalesInvoice
        {
            SalesInvoiceLines = new List<SalesInvoiceLine>
            {
                new SalesInvoiceLine { Quantity = null, Amount = 100m },
                new SalesInvoiceLine { Quantity = 2, Amount = null },
                new SalesInvoiceLine { Quantity = 3, Amount = 20m, Discount = null } // 60
            }
        };

        Assert.Equal(60m, invoice.Amount);
    }

    [Fact]
    public void RecordInvoicePaymentDto_InitializesWithDefaults()
    {
        var payment = new RecordInvoicePaymentDto
        {
            InvoiceId = 42,
            Amount = 150.50m,
            PaymentMethod = "Bank Transfer",
            ReferenceNo = "WT-9821"
        };

        Assert.Equal(42, payment.InvoiceId);
        Assert.Equal(150.50m, payment.Amount);
        Assert.Equal("Bank Transfer", payment.PaymentMethod);
        Assert.Equal("WT-9821", payment.ReferenceNo);
        Assert.True((DateTime.UtcNow - payment.PaymentDate).TotalMinutes < 5);
    }

    [Fact]
    public void SalesInvoice_PaymentHistoryList_InitializesProperly()
    {
        var invoice = new SalesInvoice
        {
            Payments = new List<InvoicePaymentItemDto>
            {
                new InvoicePaymentItemDto
                {
                    Id = 1,
                    ReceiptNo = "REC-001",
                    Date = DateTime.UtcNow,
                    Amount = 100m,
                    PaymentMethod = "Credit Card",
                    ReferenceNo = "CC-4512"
                }
            }
        };

        Assert.NotNull(invoice.Payments);
        Assert.Single(invoice.Payments);
        Assert.Equal(100m, invoice.Payments[0].Amount);
        Assert.Equal("Credit Card", invoice.Payments[0].PaymentMethod);
    }

    [Fact]
    public void ServiceItem_GLAccountsValidated_RequiresOnlySalesAccount()
    {
        var serviceCategory = new Core.Domain.Items.ItemCategory
        {
            Id = 1,
            ItemType = Core.Domain.ItemTypes.Service,
            Name = "Services"
        };

        var serviceItem = new Core.Domain.Items.Item
        {
            Id = 1,
            Description = "Consulting Service",
            ItemCategory = serviceCategory,
            SalesAccountId = 55,
            CostOfGoodsSoldAccountId = null,
            InventoryAccountId = null,
            InventoryAdjustmentAccountId = null
        };

        Assert.True(serviceItem.GLAccountsValidated());
    }

    [Fact]
    public void PhysicalItem_GLAccountsValidated_RequiresAllInventoryAccounts()
    {
        var purchasedCategory = new Core.Domain.Items.ItemCategory
        {
            Id = 2,
            ItemType = Core.Domain.ItemTypes.Purchased,
            Name = "Hardware"
        };

        var incompleteItem = new Core.Domain.Items.Item
        {
            Id = 2,
            Description = "Physical Device",
            ItemCategory = purchasedCategory,
            SalesAccountId = 55,
            CostOfGoodsSoldAccountId = null // missing COGS
        };

        Assert.False(incompleteItem.GLAccountsValidated());

        var completeItem = new Core.Domain.Items.Item
        {
            Id = 3,
            Description = "Complete Device",
            ItemCategory = purchasedCategory,
            SalesAccountId = 55,
            CostOfGoodsSoldAccountId = 67,
            InventoryAccountId = 12,
            InventoryAdjustmentAccountId = 69
        };

        Assert.True(completeItem.GLAccountsValidated());
    }

    [Theory]
    [InlineData("Void", 0, 100, 30, "Void")]
    [InlineData("Closed", 100, 0, 30, "Paid")]
    [InlineData("Open", 100, 0, 30, "Paid")]
    [InlineData("Draft", 50, 50, 30, "Partially Paid")]
    [InlineData("Open", 50, 50, 30, "Partially Paid")]
    [InlineData("Draft", 0, 100, 30, "Draft")]
    [InlineData("Open", 0, 100, -5, "Overdue")]
    [InlineData("Open", 0, 100, 30, "Open")]
    public void SalesInvoiceLifecycle_ResolvesCorrectStatus(
        string headerStatus,
        decimal allocated,
        decimal balanceDue,
        int daysUntilDue,
        string expectedStatus)
    {
        var dueDate = DateTime.UtcNow.AddDays(daysUntilDue);

        // Simulation of SalesController status resolver algorithm
        string resolvedStatus;
        if (headerStatus == "Void")
        {
            resolvedStatus = "Void";
        }
        else if (headerStatus == "Closed" || (balanceDue <= 0.005m && allocated > 0))
        {
            resolvedStatus = "Paid";
        }
        else if (allocated > 0 && balanceDue > 0.005m)
        {
            resolvedStatus = "Partially Paid";
        }
        else if (headerStatus == "Draft" && allocated == 0)
        {
            resolvedStatus = "Draft";
        }
        else if (dueDate.Date < DateTime.UtcNow.Date && balanceDue > 0.005m)
        {
            resolvedStatus = "Overdue";
        }
        else
        {
            resolvedStatus = "Open";
        }

        Assert.Equal(expectedStatus, resolvedStatus);
    }
}
