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

        // GET api/sales/getcustomerbyid/1
        [HttpGet]
        [Route("[action]/{id:int}")]
        public IActionResult GetCustomerById(int id)
        {
            try
            {
                var customer = _salesService.GetCustomerById(id);

                if (customer == null)
                {
                    return Ok();
                }

                var customerDto = new Dto.Sales.Customer()
                {
                    Id = customer.Id,
                    No = customer.No,
                    Name = customer.Party.Name,
                    Email = customer.Party.Email,
                    Phone = customer.Party.Phone,
                    Fax = customer.Party.Fax,
                    Balance = customer.Balance
                };

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
            IList<Dto.Sales.Customer> Dto = new List<Dto.Sales.Customer>();
            try
            {
                var customers = _salesService.GetCustomers();
                foreach (var customer in customers)
                {
                    var customerDto = new Dto.Sales.Customer()
                    {
                        Id = customer.Id,
                        No = customer.No,
                        Name = customer.Party.Name,
                        Email = customer.Party.Email,
                        Phone = customer.Party.Phone,
                        Fax = customer.Party.Fax,
                        Balance = customer.Balance
                    };

                    Dto.Add(customerDto);
                }

                return new ObjectResult(Dto);
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex);
            }
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult GetSalesOrders()
        {
            IList<Dto.Sales.SalesOrder> Dto = new List<Dto.Sales.SalesOrder>();

            var salesOrderDto1 = new Dto.Sales.SalesOrder()
            {
                Id = 1,
                CustomerId = 1,
                CustomerNo = "001",
                CustomerName = "John Doe",
                OrderDate = DateTime.Now,
                TotalAmount = 2500
            };
            Dto.Add(salesOrderDto1);

            var salesOrderDto2 = new Dto.Sales.SalesOrder()
            {
                Id = 2,
                CustomerId = 2,
                CustomerNo = "002",
                CustomerName = "Joe Bloggs",
                OrderDate = DateTime.Now,
                TotalAmount = 3500
            };
            Dto.Add(salesOrderDto2);

            return new ObjectResult(Dto);
            //try
            //{
            //    var salesOrders = _salesService.GetSalesOrders();

            //    foreach (var salesOrder in salesOrders)
            //    {
            //        var salesOrderDto = new Dto.Sales.SalesOrder()
            //        {
            //            Id = salesOrder.Id,
            //            CustomerId = salesOrder.CustomerId.Value,
            //            CustomerNo = salesOrder.Customer.No,
            //            CustomerName = _salesService.GetCustomerById(salesOrder.CustomerId.Value).Party.Name,
            //            OrderDate = salesOrder.Date,
            //            TotalAmount = salesOrder.SalesOrderLines.Sum(l => l.Amount)
            //        };

            //        Dto.Add(salesOrderDto);
            //    }

            //    return new ObjectResult(Dto);
            //}
            //catch(Exception ex)
            //{
            //    return new ObjectResult(ex);
            //}
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
                    TotalAmount = salesOrder.SalesOrderLines.Sum(l => l.Amount),
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
    }
}
