using System;
using System.Collections.Generic;
using System.Linq;
using Api.Data;
using Api.Tenancy;
using AutoMapper;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.TaxSystem;
using Dto.Sales;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Services.Administration;
using Services.Financial;
using Services.Inventory;
using Services.Sales;
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

    [Fact]
    public void SalesInvoiceHeader_CalculatesTotalAllocated_BalanceDue_AndIsFullPaid()
    {
        var invoice = new Core.Domain.Sales.SalesInvoiceHeader
        {
            ShippingHandlingCharge = 10m
        };
        invoice.SalesInvoiceLines.Add(new Core.Domain.Sales.SalesInvoiceLine
        {
            Quantity = 2,
            Amount = 45m,
            TaxAmount = 5m
        });

        // Grand total = Subtotal (90) + Tax (5) + Shipping (10) = 105
        Assert.Equal(105m, invoice.GrandTotal());
        Assert.Equal(0m, invoice.TotalAllocated());
        Assert.Equal(105m, invoice.BalanceDue());
        Assert.False(invoice.IsFullPaid());

        // Partial allocation of 50
        invoice.CustomerAllocations.Add(new Core.Domain.Sales.CustomerAllocation { Amount = 50m });
        Assert.Equal(50m, invoice.TotalAllocated());
        Assert.Equal(55m, invoice.BalanceDue());
        Assert.False(invoice.IsFullPaid());

        // Full allocation remaining 55
        invoice.CustomerAllocations.Add(new Core.Domain.Sales.CustomerAllocation { Amount = 55m });
        Assert.Equal(105m, invoice.TotalAllocated());
        Assert.Equal(0m, invoice.BalanceDue());
        Assert.True(invoice.IsFullPaid());
    }

    [Fact]
    public void SalesInvoicePresentation_Get_FallsBackSafely_WhenReceiptMissingOrWhitespace()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var tenant = new TenantContext();
        using var db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options, tenantContext: tenant);
        db.Database.EnsureCreated();

        var company = new Company { Name = "Presentation Co", Slug = "pres-co" };
        db.Companies.Add(company);
        db.SaveChanges();
        tenant.SetTenant(company.Id, company.Slug!, CompanyMemberRole.Owner, Array.Empty<string>());

        var domainInvoice = new Core.Domain.Sales.SalesInvoiceHeader
        {
            Id = 10,
            CompanyId = company.Id,
            CustomerId = 5,
            No = "INV-100",
            Date = new DateTime(2026, 11, 1),
            Status = SalesInvoiceStatus.Open,
            Customer = new Core.Domain.Sales.Customer
            {
                Id = 5,
                Party = new Core.Domain.Party { Name = "Client A", Email = "billing@client-a.test" }
            }
        };
        domainInvoice.SalesInvoiceLines.Add(new Core.Domain.Sales.SalesInvoiceLine
        {
            Id = 1,
            ItemId = 1,
            MeasurementId = 1,
            Quantity = 1,
            Amount = 100m,
            Description = "Service"
        });
        // Allocation 1: missing/inaccessible receipt (SalesReceiptHeader == null at mapper boundary)
        domainInvoice.CustomerAllocations.Add(new Core.Domain.Sales.CustomerAllocation
        {
            Id = 71,
            CustomerId = 5,
            SalesInvoiceHeaderId = 10,
            SalesReceiptHeaderId = 999,
            Date = new DateTime(2026, 11, 3),
            Amount = 40m,
            SalesReceiptHeader = null!
        });
        // Allocation 2: receipt present with whitespace-only strings
        domainInvoice.CustomerAllocations.Add(new Core.Domain.Sales.CustomerAllocation
        {
            Id = 72,
            CustomerId = 5,
            SalesInvoiceHeaderId = 10,
            SalesReceiptHeaderId = 888,
            Date = new DateTime(2026, 11, 2),
            Amount = 25m,
            SalesReceiptHeader = new Core.Domain.Sales.SalesReceiptHeader
            {
                Id = 888,
                No = "   ",
                PaymentMethod = "   ",
                ReferenceNo = "   ",
                Memo = "   "
            }
        });

        var sales = new Mock<ISalesService>();
        sales.Setup(s => s.GetSalesInvoiceById(10)).Returns(domainInvoice);

        var admin = new Mock<IAdministrationService>();
        admin.Setup(a => a.GetPaymentTerms()).Returns(Array.Empty<PaymentTerm>());
        admin.Setup(a => a.GetCompanyBySlug(company.Slug!)).Returns(company);
        admin.Setup(a => a.GetCompanies()).Returns(new[] { company });

        var presentation = new Api.Service.SalesInvoicePresentation(sales.Object, admin.Object, db, tenant);
        var dto = presentation.Get(10);

        Assert.NotNull(dto);
        Assert.NotNull(dto!.Payments);
        Assert.Equal(2, dto.Payments!.Count);

        var missingReceiptItem = dto.Payments.Single(p => p.Id == 71);
        Assert.Null(missingReceiptItem.ReceiptNo);
        Assert.Null(missingReceiptItem.ReferenceNo);
        Assert.Null(missingReceiptItem.Notes);
        Assert.Equal("Payment", missingReceiptItem.PaymentMethod);
        Assert.Equal(40m, missingReceiptItem.Amount);

        var blankReceiptItem = dto.Payments.Single(p => p.Id == 72);
        Assert.Null(blankReceiptItem.ReceiptNo);
        Assert.Null(blankReceiptItem.ReferenceNo);
        Assert.Null(blankReceiptItem.Notes);
        Assert.Equal("Payment", blankReceiptItem.PaymentMethod);
        Assert.Equal(25m, blankReceiptItem.Amount);
    }

    [Fact]
    public void SalesService_GetSalesInvoiceById_LoadsTenantFilteredReceipts_AndPreservesExistingNavigation()
    {
        using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=True");
        connection.Open();
        var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options;
        var tenant = new TenantContext();

        int companyId;
        int invoiceId;
        int receiptId;
        using (var writeDb = new ApiDbContext(options, tenantContext: tenant))
        {
            writeDb.Database.EnsureCreated();
            var company = new Company { Name = "Tenant A", Slug = "tenant-a" };
            writeDb.Companies.Add(company);
            writeDb.SaveChanges();
            companyId = company.Id;
            tenant.SetTenant(companyId, company.Slug!, CompanyMemberRole.Owner, Array.Empty<string>());

            var accountClass = writeDb.AccountClasses.FirstOrDefault() ?? writeDb.AccountClasses.Add(new AccountClass { Name = "Assets" }).Entity;
            var cashAccount = new Account { CompanyId = companyId, AccountClass = accountClass, AccountCode = "10100", AccountName = "Cash", IsCash = true };
            var salesAccount = new Account { CompanyId = companyId, AccountClass = accountClass, AccountCode = "40100", AccountName = "Sales" };
            var party = new Party { CompanyId = companyId, Name = "Acme Client" };
            var category = new ItemCategory { CompanyId = companyId, Name = "Services", ItemType = ItemTypes.Service };
            var uom = new Measurement { CompanyId = companyId, Code = "EA", Description = "Each" };
            writeDb.AddRange(cashAccount, salesAccount, party, category, uom);
            writeDb.SaveChanges();

            var customer = new Core.Domain.Sales.Customer { CompanyId = companyId, PartyId = party.Id, No = "CUS-1" };
            var item = new Item { CompanyId = companyId, ItemCategoryId = category.Id, SmallestMeasurementId = uom.Id, SellMeasurementId = uom.Id, No = "ITM-1", Description = "Consulting", SalesAccountId = salesAccount.Id };
            writeDb.AddRange(customer, item);
            writeDb.SaveChanges();

            var inv = new Core.Domain.Sales.SalesInvoiceHeader
            {
                CompanyId = companyId,
                CustomerId = customer.Id,
                No = "INV-001",
                Date = new DateTime(2026, 11, 10),
                Status = SalesInvoiceStatus.Closed
            };
            inv.SalesInvoiceLines.Add(new Core.Domain.Sales.SalesInvoiceLine
            {
                CompanyId = companyId,
                ItemId = item.Id,
                MeasurementId = uom.Id,
                Quantity = 1,
                Amount = 100m
            });
            writeDb.SalesInvoiceHeaders.Add(inv);
            writeDb.SaveChanges();
            invoiceId = inv.Id;

            var rcpt = new Core.Domain.Sales.SalesReceiptHeader
            {
                CompanyId = companyId,
                CustomerId = customer.Id,
                AccountToDebitId = cashAccount.Id,
                Date = new DateTime(2026, 11, 11),
                Amount = 100m,
                No = "RCPT-501",
                PaymentMethod = "Wire",
                ReferenceNo = "TX-501",
                Memo = "Settled"
            };
            writeDb.SalesReceiptHeaders.Add(rcpt);
            writeDb.SaveChanges();
            receiptId = rcpt.Id;

            writeDb.CustomerAllocations.Add(new Core.Domain.Sales.CustomerAllocation
            {
                CompanyId = companyId,
                CustomerId = customer.Id,
                SalesInvoiceHeaderId = invoiceId,
                SalesReceiptHeaderId = receiptId,
                Date = rcpt.Date,
                Amount = 100m
            });
            writeDb.SaveChanges();
        }

        // 1. Fresh DbContext: EF tracking has not seen SalesReceiptHeader; GetSalesInvoiceById hydrates it cleanly as Unchanged.
        using (var readDb = new ApiDbContext(options, tenantContext: tenant))
        {
            var service = CreateSalesServiceWithEfRepos(readDb);
            var loaded = service.GetSalesInvoiceById(invoiceId);
            Assert.NotNull(loaded);
            var alloc = Assert.Single(loaded!.CustomerAllocations);
            Assert.NotNull(alloc.SalesReceiptHeader);
            Assert.Equal("RCPT-501", alloc.SalesReceiptHeader.No);
            Assert.Equal("TX-501", alloc.SalesReceiptHeader.ReferenceNo);
            Assert.Equal("Wire", alloc.SalesReceiptHeader.PaymentMethod);
            Assert.Equal("Settled", alloc.SalesReceiptHeader.Memo);
            Assert.Equal(EntityState.Unchanged, readDb.Entry(alloc.SalesReceiptHeader).State);

            // Switching active tenant hides both invoice and receipt via ApiDbContext global tenant filters.
            tenant.SetTenant(companyId + 999, "other-tenant", CompanyMemberRole.Owner, Array.Empty<string>());
            Assert.Null(service.GetSalesInvoiceById(invoiceId));
        }

        // 2. Repository boundary: missing/filtered receipt does not overwrite an already-populated navigation, and leaves unset ones null.
        var existingReceipt = new Core.Domain.Sales.SalesReceiptHeader { Id = 10, No = "KEEP-ME", ReferenceNo = "REF-KEEP" };
        var populatedAlloc = new Core.Domain.Sales.CustomerAllocation { Id = 1, SalesReceiptHeaderId = 10, SalesReceiptHeader = existingReceipt };
        var missingAlloc = new Core.Domain.Sales.CustomerAllocation { Id = 2, SalesReceiptHeaderId = 20, SalesReceiptHeader = null! };
        var header = new Core.Domain.Sales.SalesInvoiceHeader
        {
            Id = 55,
            CustomerAllocations = new List<Core.Domain.Sales.CustomerAllocation> { populatedAlloc, missingAlloc },
            SalesInvoiceLines = new List<Core.Domain.Sales.SalesInvoiceLine>()
        };

        var invRepo = new Mock<IRepository<Core.Domain.Sales.SalesInvoiceHeader>>();
        invRepo.Setup(r => r.GetAllIncluding(It.IsAny<System.Linq.Expressions.Expression<Func<Core.Domain.Sales.SalesInvoiceHeader, object?>>[]>()))
            .Returns(new[] { header }.AsQueryable());
        var rcptRepo = new Mock<IRepository<Core.Domain.Sales.SalesReceiptHeader>>();
        rcptRepo.Setup(r => r.Table).Returns(Array.Empty<Core.Domain.Sales.SalesReceiptHeader>().AsQueryable());

        var mockService = new SalesService(
            Mock.Of<IFinancialService>(),
            Mock.Of<IInventoryService>(),
            Mock.Of<IStockService>(),
            Mock.Of<IRepository<Core.Domain.Sales.SalesOrderHeader>>(),
            Mock.Of<IRepository<Core.Domain.Sales.SalesOrderLine>>(),
            invRepo.Object,
            rcptRepo.Object,
            Mock.Of<IRepository<Core.Domain.Sales.Customer>>(),
            Mock.Of<IRepository<Account>>(),
            Mock.Of<IRepository<Item>>(),
            Mock.Of<IRepository<Measurement>>(),
            Mock.Of<IRepository<SequenceNumber>>(),
            Mock.Of<IRepository<PaymentTerm>>(),
            Mock.Of<IRepository<Core.Domain.Sales.SalesDeliveryHeader>>(),
            Mock.Of<IRepository<Bank>>(),
            Mock.Of<IRepository<GeneralLedgerSetting>>(),
            Mock.Of<IRepository<Contact>>(),
            Mock.Of<IRepository<TaxGroup>>(),
            Mock.Of<IRepository<Core.Domain.Sales.SalesQuoteHeader>>(),
            Mock.Of<ISalesOrderRepository>(),
            Mock.Of<IRepository<CustomerContact>>(),
            Mock.Of<IRepository<VendorContact>>(),
            NullLogger<SalesService>.Instance,
            Mock.Of<IMapper>(),
            Mock.Of<IRepository<DocumentNumberSeries>>(),
            Mock.Of<IRepository<FinancialYear>>(),
            Mock.Of<IRepository<CompanySetting>>());

        var result = mockService.GetSalesInvoiceById(55);
        Assert.NotNull(result);
        Assert.Same(existingReceipt, populatedAlloc.SalesReceiptHeader);
        Assert.Null(missingAlloc.SalesReceiptHeader);
    }

    private static SalesService CreateSalesServiceWithEfRepos(ApiDbContext db) =>
        new(
            Mock.Of<IFinancialService>(),
            Mock.Of<IInventoryService>(),
            Mock.Of<IStockService>(),
            new EfRepository<Core.Domain.Sales.SalesOrderHeader>(NullLogger<Core.Domain.Sales.SalesOrderHeader>.Instance, db),
            new EfRepository<Core.Domain.Sales.SalesOrderLine>(NullLogger<Core.Domain.Sales.SalesOrderLine>.Instance, db),
            new EfRepository<Core.Domain.Sales.SalesInvoiceHeader>(NullLogger<Core.Domain.Sales.SalesInvoiceHeader>.Instance, db),
            new EfRepository<Core.Domain.Sales.SalesReceiptHeader>(NullLogger<Core.Domain.Sales.SalesReceiptHeader>.Instance, db),
            new EfRepository<Core.Domain.Sales.Customer>(NullLogger<Core.Domain.Sales.Customer>.Instance, db),
            new EfRepository<Account>(NullLogger<Account>.Instance, db),
            new EfRepository<Item>(NullLogger<Item>.Instance, db),
            new EfRepository<Measurement>(NullLogger<Measurement>.Instance, db),
            new EfRepository<SequenceNumber>(NullLogger<SequenceNumber>.Instance, db),
            new EfRepository<PaymentTerm>(NullLogger<PaymentTerm>.Instance, db),
            new EfRepository<Core.Domain.Sales.SalesDeliveryHeader>(NullLogger<Core.Domain.Sales.SalesDeliveryHeader>.Instance, db),
            new EfRepository<Bank>(NullLogger<Bank>.Instance, db),
            new EfRepository<GeneralLedgerSetting>(NullLogger<GeneralLedgerSetting>.Instance, db),
            new EfRepository<Contact>(NullLogger<Contact>.Instance, db),
            new EfRepository<TaxGroup>(NullLogger<TaxGroup>.Instance, db),
            new EfRepository<Core.Domain.Sales.SalesQuoteHeader>(NullLogger<Core.Domain.Sales.SalesQuoteHeader>.Instance, db),
            Mock.Of<ISalesOrderRepository>(),
            new EfRepository<CustomerContact>(NullLogger<CustomerContact>.Instance, db),
            new EfRepository<VendorContact>(NullLogger<VendorContact>.Instance, db),
            NullLogger<SalesService>.Instance,
            Mock.Of<IMapper>(),
            new EfRepository<DocumentNumberSeries>(NullLogger<DocumentNumberSeries>.Instance, db),
            new EfRepository<FinancialYear>(NullLogger<FinancialYear>.Instance, db),
            new EfRepository<CompanySetting>(NullLogger<CompanySetting>.Instance, db));
}
