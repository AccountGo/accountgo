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

                var customerModel = new Model.Sales.Customer()
                {
                    Id = customer.Id,
                    No = customer.No,
                    Name = customer.Party.Name,
                    Email = customer.Party.Email,
                    Phone = customer.Party.Phone,
                    Fax = customer.Party.Fax,
                    Balance = customer.Balance
                };

                return new ObjectResult(customerModel);
            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult(ex);
            }
        }

        [HttpPost]
        [Route("[action]")]
        public IActionResult CreateCustomer([FromBody]Model.Sales.CreateCustomer model)
        {
            try
            {
                var customer = new Core.Domain.Sales.Customer()
                {
                    Party = new Core.Domain.Party()
                    {
                        PartyType = Core.Domain.PartyTypes.Customer,
                        Name = model.Name,
                        Phone = model.Phone,
                    },
                };

                _salesService.AddCustomer(customer);

                var customerModel = new Model.Sales.Customer();
                customerModel.Id = customer.Id;
                customerModel.No = customer.No;
                customerModel.Name = customer.Party.Name;
                customerModel.Phone = customer.Party.Phone;

                return new ObjectResult(customerModel);
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
            IList<Model.Sales.Customer> model = new List<Model.Sales.Customer>();
            try
            {
                var customers = _salesService.GetCustomers();
                foreach (var customer in customers)
                {
                    var customerModel = new Model.Sales.Customer()
                    {
                        Id = customer.Id,
                        No = customer.No,
                        Name = customer.Party.Name,
                        Email = customer.Party.Email,
                        Phone = customer.Party.Phone,
                        Fax = customer.Party.Fax,
                        Balance = customer.Balance
                    };

                    model.Add(customerModel);
                }

                return new ObjectResult(model);
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
            IList<Model.Sales.SalesOrder> model = new List<Model.Sales.SalesOrder>();

            var salesOrderModel1 = new Model.Sales.SalesOrder()
            {
                Id = 1,
                CustomerId = 1,
                CustomerNo = "001",
                CustomerName = "John Doe",
                OrderDate = DateTime.Now,
                TotalAmount = 2500
            };
            model.Add(salesOrderModel1);

            var salesOrderModel2 = new Model.Sales.SalesOrder()
            {
                Id = 2,
                CustomerId = 2,
                CustomerNo = "002",
                CustomerName = "Joe Bloggs",
                OrderDate = DateTime.Now,
                TotalAmount = 3500
            };
            model.Add(salesOrderModel2);

            return new ObjectResult(model);
            //try
            //{
            //    var salesOrders = _salesService.GetSalesOrders();

            //    foreach (var salesOrder in salesOrders)
            //    {
            //        var salesOrderModel = new Model.Sales.SalesOrder()
            //        {
            //            Id = salesOrder.Id,
            //            CustomerId = salesOrder.CustomerId.Value,
            //            CustomerNo = salesOrder.Customer.No,
            //            CustomerName = _salesService.GetCustomerById(salesOrder.CustomerId.Value).Party.Name,
            //            OrderDate = salesOrder.Date,
            //            TotalAmount = salesOrder.SalesOrderLines.Sum(l => l.Amount)
            //        };

            //        model.Add(salesOrderModel);
            //    }

            //    return new ObjectResult(model);
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
            IList<Model.Sales.SalesOrder> model = new List<Model.Sales.SalesOrder>();

            try
            {
                var salesOrder = _salesService.GetSalesOrderById(id);

                var salesOrderModel = new Model.Sales.SalesOrder()
                {
                    Id = salesOrder.Id,
                    CustomerId = salesOrder.CustomerId.Value,
                    CustomerNo = salesOrder.Customer.No,
                    CustomerName = _salesService.GetCustomerById(salesOrder.CustomerId.Value).Party.Name,
                    OrderDate = salesOrder.Date,
                    TotalAmount = salesOrder.SalesOrderLines.Sum(l => l.Amount),
                    SalesOrderLines = new List<Model.Sales.SalesOrderLine>()
                };

                foreach(var line in salesOrder.SalesOrderLines)
                {
                    var lineModel = new Model.Sales.SalesOrderLine();
                    lineModel.Id = line.Id;
                    lineModel.Amount = line.Amount;
                    lineModel.Discount = line.Discount;
                    lineModel.Quantity = line.Quantity;
                    lineModel.ItemId = line.ItemId;
                    lineModel.ItemDescription = line.Item.Description;
                    lineModel.MeasurementId = line.MeasurementId;
                    lineModel.MeasurementDescription = line.Measurement.Description;

                    salesOrderModel.SalesOrderLines.Add(lineModel);
                }

                return new ObjectResult(salesOrderModel);
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex);
            }
        }

        [HttpPost]
        [Route("[action]")]
        public IActionResult CreateSalesOrder([FromBody]Model.Sales.SalesOrder model)
        {
            try
            {
                var salesOrder = new Core.Domain.Sales.SalesOrderHeader()
                {
                    CustomerId = model.CustomerId,
                    Date = model.OrderDate,
                };

                foreach(var line in model.SalesOrderLines)
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

                model.Id = salesOrder.Id;

                return new ObjectResult(model);
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex);
            }
        }
    }
}
