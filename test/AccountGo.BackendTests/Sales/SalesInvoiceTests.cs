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
}
