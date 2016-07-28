using Dto.Common;
using Microsoft.AspNetCore.Mvc;
using Services.Administration;
using Services.Sales;
using System;
using System.Collections.Generic;
using System.Linq;

// For more information on enabling Web API for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class SalesController : Controller
    {
        private readonly IAdministrationService _adminService;
        private readonly ISalesService _salesService;

        public SalesController(IAdministrationService adminService,
            ISalesService salesService)
        {
            _adminService = adminService;
            _salesService = salesService;
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Customer(int id)
        {
            try
            {
                var customer = _salesService.GetCustomerById(id);

                var customerDto = new Dto.Sales.Customer()
                {
                    Id = customer.Id,
                    No = customer.No,                  
                    AccountsReceivableId = customer.AccountsReceivableAccountId.GetValueOrDefault(),
                    SalesAccountId = customer.SalesAccountId.GetValueOrDefault(),
                    PrepaymentAccountId = customer.CustomerAdvancesAccountId.GetValueOrDefault(),
                    SalesDiscountAccountId = customer.SalesDiscountAccountId.GetValueOrDefault(),
                    PaymentTermId = customer.PaymentTermId.GetValueOrDefault(),
                    TaxGroupId = customer.TaxGroupId.GetValueOrDefault()
                };
                customerDto.Name = customer.Party.Name;
                customerDto.Email = customer.Party.Email;
                customerDto.Website = customer.Party.Website;
                customerDto.Phone = customer.Party.Phone;
                customerDto.Fax = customer.Party.Fax;

                if (customer.PrimaryContact != null) {
                    customerDto.PrimaryContact.FirstName = customer.PrimaryContact.FirstName;
                    customerDto.PrimaryContact.LastName = customer.PrimaryContact.LastName;
                    customerDto.PrimaryContact.Party.Email = customer.PrimaryContact.Party.Email;
                    customerDto.PrimaryContact.Party.Phone = customer.PrimaryContact.Party.Phone;
                    customerDto.PrimaryContact.Party.Fax = customer.PrimaryContact.Party.Fax;
                    customerDto.PrimaryContact.Party.Website = customer.PrimaryContact.Party.Website;
                    customerDto.PrimaryContact.Party.Name = customer.PrimaryContact.Party.Name;
                }

                return new ObjectResult(customerDto);
            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult(ex);
            }
        }

        [HttpPost]
        [Route("[action]")]
        public IActionResult CreateCustomer([FromBody]Dto.Sales.CreateCustomer Dto)
        {
            try
            {
                var customer = new Core.Domain.Sales.Customer()
                {
                    Party = new Core.Domain.Party()
                    {
                        PartyType = Core.Domain.PartyTypes.Customer,
                        Name = Dto.Name,
                        Phone = Dto.Phone,
                    },
                };

                _salesService.AddCustomer(customer);

                var customerDto = new Dto.Sales.Customer();
                customerDto.Id = customer.Id;
                customerDto.No = customer.No;
                customerDto.Name = customer.Party.Name;
                customerDto.Phone = customer.Party.Phone;

                return new ObjectResult(customerDto);
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex);
            }
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Customers()
        {
            IList<Dto.Sales.Customer> customersDto = new List<Dto.Sales.Customer>();
            try
            {
                var customers = _salesService.GetCustomers().Where(p => p.Party != null);
                foreach (var customer in customers)
                {
                    var customerDto = new Dto.Sales.Customer()
                    {
                        Id = customer.Id,
                        No = customer.No
                    };

                    customerDto.Name = customer.Party.Name;
                    customerDto.Email = customer.Party.Email;
                    customerDto.Website = customer.Party.Website;
                    customerDto.Phone = customer.Party.Phone;
                    customerDto.Fax = customer.Party.Fax;

                    customersDto.Add(customerDto);
                }

                return new ObjectResult(customersDto);
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex);
            }
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult SalesOrders()
        {
            var salesOrders = _salesService.GetSalesOrders();
            IList<Dto.Sales.SalesOrder> salesOrdersDto = new List<Dto.Sales.SalesOrder>();

            foreach (var salesOrder in salesOrders)
            {
                var salesOrderDto = new Dto.Sales.SalesOrder()
                {
                    Id = salesOrder.Id,
                    CustomerId = salesOrder.CustomerId.Value,
                    CustomerNo = salesOrder.Customer.No,
                    CustomerName = salesOrder.Customer.Party.Name,
                    OrderDate = salesOrder.Date,
                    Amount = salesOrder.SalesOrderLines.Sum(l => l.Amount)
                };

                salesOrdersDto.Add(salesOrderDto);
            }

            return new ObjectResult(salesOrdersDto);
        }

        [HttpGet]
        [Route("[action]/{id:int}")]
        public IActionResult GetSalesOrderById(int id)
        {
            IList<Dto.Sales.SalesOrder> Dto = new List<Dto.Sales.SalesOrder>();

            try
            {
                var salesOrder = _salesService.GetSalesOrderById(id);

                var salesOrderDto = new Dto.Sales.SalesOrder()
                {
                    Id = salesOrder.Id,
                    CustomerId = salesOrder.CustomerId.Value,
                    CustomerNo = salesOrder.Customer.No,
                    CustomerName = _salesService.GetCustomerById(salesOrder.CustomerId.Value).Party.Name,
                    OrderDate = salesOrder.Date,
                    Amount = salesOrder.SalesOrderLines.Sum(l => l.Amount),
                    SalesOrderLines = new List<Dto.Sales.SalesOrderLine>()
                };

                foreach (var line in salesOrder.SalesOrderLines)
                {
                    var lineDto = new Dto.Sales.SalesOrderLine();
                    lineDto.Id = line.Id;
                    lineDto.Amount = line.Amount;
                    lineDto.Discount = line.Discount;
                    lineDto.Quantity = line.Quantity;
                    lineDto.ItemId = line.ItemId;
                    lineDto.ItemDescription = line.Item.Description;
                    lineDto.MeasurementId = line.MeasurementId;
                    lineDto.MeasurementDescription = line.Measurement.Description;

                    salesOrderDto.SalesOrderLines.Add(lineDto);
                }

                return new ObjectResult(salesOrderDto);
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex);
            }
        }

        [HttpPost]
        [Route("[action]")]
        public IActionResult CreateSalesOrder([FromBody]Dto.Sales.SalesOrder Dto)
        {
            try
            {
                var salesOrder = new Core.Domain.Sales.SalesOrderHeader()
                {
                    CustomerId = Dto.CustomerId,
                    Date = Dto.OrderDate,
                };

                foreach (var line in Dto.SalesOrderLines)
                {
                    var salesOrderLine = new Core.Domain.Sales.SalesOrderLine();
                    salesOrderLine.Amount = line.Amount;
                    salesOrderLine.Discount = line.Discount;
                    salesOrderLine.Quantity = line.Quantity;
                    salesOrderLine.ItemId = line.ItemId;
                    salesOrderLine.MeasurementId = line.MeasurementId;

                    salesOrder.SalesOrderLines.Add(salesOrderLine);
                }

                _salesService.AddSalesOrder(salesOrder, true);

                Dto.Id = salesOrder.Id;

                return new ObjectResult(Dto);
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex);
            }
        }

        [HttpPost]
        [Route("[action]")]
        public IActionResult AddQuotation([FromBody]Dto.Sales.SalesQuotation Dto)
        {
            try
            {
                var salesQuote = new Core.Domain.Sales.SalesQuoteHeader()
                {
                    CustomerId = Dto.CustomerId,
                    Date = Dto.QuotationDate,
                };

                foreach (var line in Dto.SalesQuotationLines)
                {
                    var salesQuoteLine = new Core.Domain.Sales.SalesQuoteLine();
                    salesQuoteLine.Amount = line.Amount == null ? 0 : line.Amount.Value;
                    salesQuoteLine.Discount = line.Discount == null ? 0 : line.Discount.Value;
                    salesQuoteLine.Quantity = line.Quantity == null ? 0 : line.Quantity.Value;
                    salesQuoteLine.ItemId = line.ItemId;
                    salesQuoteLine.MeasurementId = line.MeasurementId;

                    salesQuote.SalesQuoteLines.Add(salesQuoteLine);
                }

                _salesService.AddSalesQuote(salesQuote);

                return new ObjectResult(Dto);
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex);
            }
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Quotations()
        {
            var quotes = _salesService.GetSalesQuotes();

            var quoteDtos = new List<Dto.Sales.SalesQuotation>();

            foreach (var quote in quotes) {
                var quoteDto = new Dto.Sales.SalesQuotation()
                {
                    CustomerId = quote.CustomerId,
                    CustomerName = quote.Customer.Party.Name,
                    PaymentTermId = quote.CustomerId,
                    QuotationDate = quote.Date,                 
                };

                foreach (var line in quote.SalesQuoteLines) {
                    var lineDto = new Dto.Sales.SalesQuotationLine()
                    {
                        ItemId = line.ItemId,
                        MeasurementId = line.MeasurementId,
                        Quantity = line.Quantity,
                        Amount = line.Amount,
                        Discount = line.Discount
                    };
                    quoteDto.SalesQuotationLines.Add(lineDto);
                }

                quoteDtos.Add(quoteDto);
            }

            return new ObjectResult(quoteDtos);
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult SalesInvoices()
        {
            var salesInvoices = _salesService.GetSalesInvoices();
            IList<Dto.Sales.SalesInvoice> salesInvoicesDto = new List<Dto.Sales.SalesInvoice>();

            foreach (var salesInvoice in salesInvoices)
            {
                var salesInvoiceDto = new Dto.Sales.SalesInvoice()
                {
                    Id = salesInvoice.Id,
                    CustomerId = salesInvoice.CustomerId,
                    CustomerName = salesInvoice.Customer.Party.Name,
                    InvoiceDate = salesInvoice.Date,
                    TotalAmount = salesInvoice.SalesInvoiceLines.Sum(l => l.Amount)
                };

                salesInvoicesDto.Add(salesInvoiceDto);
            }

            return new ObjectResult(salesInvoicesDto);
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult SalesReceipts()
        {
            var salesReceipts = _salesService.GetSalesReceipts();
            IList<Dto.Sales.SalesReceipt> salesReceiptsDto = new List<Dto.Sales.SalesReceipt>();

            foreach (var salesReceipt in salesReceipts)
            {
                var salesReceiptDto = new Dto.Sales.SalesReceipt()
                {
                    Id = salesReceipt.Id,
                    ReceiptNo = salesReceipt.No,
                    CustomerId = salesReceipt.CustomerId,
                    CustomerName = salesReceipt.Customer.Party.Name,
                    ReceiptDate = salesReceipt.Date,
                    Amount = salesReceipt.Amount,
                    RemainingAmountToAllocate = salesReceipt.AvailableAmountToAllocate
                };

                salesReceiptsDto.Add(salesReceiptDto);
            }

            return new ObjectResult(salesReceiptsDto);
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult SalesReceipt(int id)
        {
            var salesReceipt = _salesService.GetSalesReceiptById(id);
            var salesReceiptDto = new Dto.Sales.SalesReceipt()
            {
                Id = salesReceipt.Id,
                ReceiptNo = salesReceipt.No,
                CustomerId = salesReceipt.CustomerId,
                CustomerName = salesReceipt.Customer.Party.Name,
                ReceiptDate = salesReceipt.Date,
                Amount = salesReceipt.Amount,
                RemainingAmountToAllocate = salesReceipt.AvailableAmountToAllocate
            };

            return new ObjectResult(salesReceiptDto);
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult CustomerInvoices(int id)
        {
            try
            {
                var invoices = _salesService.GetCustomerInvoices(id);

                var invoicesDto = new HashSet<Dto.Sales.SalesInvoice>();

                foreach (var invoice in invoices)
                {
                    var invoiceDto = new Dto.Sales.SalesInvoice()
                    {
                        Id = invoice.Id,
                        InvoiceDate = invoice.Date,
                        CustomerId = invoice.CustomerId,
                        TotalAmount = invoice.ComputeTotalAmount(),
                        TotalAllocatedAmount = (double)invoice.CustomerAllocations.Sum(i => i.Amount)
                    };

                    invoicesDto.Add(invoiceDto);
                }

                return new ObjectResult(invoicesDto);
            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult(ex);
            }
        }
    }
}