using Microsoft.AspNetCore.Mvc;
using Services.Administration;
using Services.Financial;
using Services.Sales;
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain;
using Core.Domain.Sales;
using Services.Inventory;
using Dto.Sales;
using Services.TaxSystem;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class SalesController : BaseController
    {
        private readonly IAdministrationService _adminService;
        private readonly ISalesService _salesService;
        private readonly IFinancialService _financialService;
        private readonly IInventoryService _inventoryService;
        private readonly ITaxService _taxService;

        public SalesController(IAdministrationService adminService,
            ISalesService salesService,
            IFinancialService financialService, IInventoryService inventoryService, ITaxService taxService)
        {
            _adminService = adminService;
            _salesService = salesService;
            _financialService = financialService;
            _inventoryService = inventoryService;
            _taxService = taxService;
        }

        [HttpPost]
        [Route("[action]")]
        public IActionResult SaveCustomer([FromBody]Dto.Sales.Customer customerDto)
        {
            bool isNew = customerDto.Id == 0;
            Core.Domain.Sales.Customer customer = null;

            if (isNew)
            {
                customer = new Core.Domain.Sales.Customer();

                customer.Party = new Core.Domain.Party()
                {
                    PartyType = Core.Domain.PartyTypes.Customer,
                };

                customer.PrimaryContact = new Core.Domain.Contact()
                {
                    ContactType = Core.Domain.ContactTypes.Customer,
                    Party = new Core.Domain.Party()
                    {
                        PartyType = Core.Domain.PartyTypes.Contact
                    }
                };
            }
            else
            {
                customer = _salesService.GetCustomerById(customerDto.Id);
            }

            customer.No = customerDto.No;
            customer.Party.Name = customerDto.Name;
            customer.Party.Phone = customerDto.Phone;
            customer.Party.Email = customerDto.Email;
            customer.Party.Fax = customerDto.Fax;
            customer.Party.Website = customerDto.Website;
            customer.PrimaryContact.FirstName = customerDto.PrimaryContact.FirstName;
            customer.PrimaryContact.LastName = customerDto.PrimaryContact.LastName;
            customer.PrimaryContact.Party.Name = customerDto.PrimaryContact.Party.Name;
            customer.PrimaryContact.Party.Phone = customerDto.PrimaryContact.Party.Phone;
            customer.PrimaryContact.Party.Email = customerDto.PrimaryContact.Party.Email;
            customer.PrimaryContact.Party.Fax = customerDto.PrimaryContact.Party.Fax;
            customer.PrimaryContact.Party.Website = customerDto.PrimaryContact.Party.Website;
            customer.AccountsReceivableAccountId = customerDto.AccountsReceivableId;
            customer.SalesAccountId = customerDto.SalesAccountId;
            customer.CustomerAdvancesAccountId = customerDto.PrepaymentAccountId;
            customer.SalesDiscountAccountId = customerDto.SalesDiscountAccountId;
            customer.PaymentTermId = customerDto.PaymentTermId;
            customer.TaxGroupId = customerDto.TaxGroupId;
            customer.ModifiedBy = GetUserNameFromRequestHeader();

            if (isNew)
                _salesService.AddCustomer(customer);
            else
                _salesService.UpdateCustomer(customer);

            return Ok();
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

                if (customer.PrimaryContact != null)
                {
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
                        No = customer.No,
                    };

                    customerDto.Name = customer.Party.Name;
                    customerDto.Email = customer.Party.Email;
                    customerDto.Website = customer.Party.Website;
                    customerDto.Phone = customer.Party.Phone;
                    customerDto.Fax = customer.Party.Fax;
                    customerDto.Balance = customer.Balance;
                    customerDto.PrepaymentAccountId = customer.CustomerAdvancesAccountId;
                    customerDto.Contact = customer.PrimaryContact.FirstName + " " + customer.PrimaryContact.LastName;
                    customerDto.TaxGroup = customer.TaxGroup == null ? string.Empty : customer.TaxGroup.Description;
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


            try
            {
                foreach (var salesOrder in salesOrders)
                {
                    var salesOrderDto = new Dto.Sales.SalesOrder()
                    {
                        Id = salesOrder.Id,
                        PaymentTermId = salesOrder.PaymentTermId,
                        CustomerId = salesOrder.CustomerId.Value,
                        CustomerNo = salesOrder.Customer.No,
                        CustomerName = salesOrder.Customer.Party.Name,
                        OrderDate = salesOrder.Date,
                        ReferenceNo = salesOrder.ReferenceNo,
                        StatusId = (int)salesOrder.Status.GetValueOrDefault(),
                        No = salesOrder.No
                    };

                    foreach (var line in salesOrder.SalesOrderLines)
                    {
                        var lineDto = new Dto.Sales.SalesOrderLine()
                        {
                            ItemId = line.ItemId,
                            MeasurementId = line.MeasurementId,
                            Quantity = line.Quantity,
                            Amount = line.Amount,
                            Discount = line.Discount,
                            RemainingQtyToInvoice = line.GetRemainingQtyToInvoice()
                        };
                        salesOrderDto.SalesOrderLines.Add(lineDto);
                    }

                    salesOrdersDto.Add(salesOrderDto);
                }

                return new ObjectResult(salesOrdersDto);
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex);
            }
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult SalesOrder(int id)
        {
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
                    PaymentTermId = salesOrder.PaymentTermId,
                    ReferenceNo = salesOrder.ReferenceNo,
                    StatusId = (int)salesOrder.Status,
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
                    lineDto.RemainingQtyToInvoice = line.GetRemainingQtyToInvoice();

                    salesOrderDto.SalesOrderLines.Add(lineDto);
                }

                return new ObjectResult(salesOrderDto);
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex);
            }
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult SalesInvoice(int id)
        {
            try
            {
                var salesInvoice = _salesService.GetSalesInvoiceById(id);

                var salesOrderDto = new Dto.Sales.SalesInvoice()
                {
                    Id = salesInvoice.Id,
                    CustomerId = salesInvoice.CustomerId,
                    CustomerName = salesInvoice.Customer.Party.Name,
                    InvoiceDate = salesInvoice.Date,
                    SalesInvoiceLines = new List<Dto.Sales.SalesInvoiceLine>(),
                    PaymentTermId = salesInvoice.PaymentTermId,
                    ReferenceNo = salesInvoice.ReferenceNo,
                    Posted = salesInvoice.GeneralLedgerHeaderId != null
                };

                foreach (var line in salesInvoice.SalesInvoiceLines)
                {
                    var lineDto = new Dto.Sales.SalesInvoiceLine();
                    lineDto.Id = line.Id;
                    lineDto.Amount = line.Amount;
                    lineDto.Discount = line.Discount;
                    lineDto.Quantity = line.Quantity;
                    lineDto.ItemId = line.ItemId;
                    lineDto.MeasurementId = line.MeasurementId;

                    salesOrderDto.SalesInvoiceLines.Add(lineDto);
                }

                // is this journal entry ready for posting?
                if (!salesOrderDto.Posted && salesOrderDto.SalesInvoiceLines.Count >= 1)
                {
                    salesOrderDto.ReadyForPosting = true;
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
        public IActionResult AddSalesOrder([FromBody]Dto.Sales.SalesOrder salesorderDto)
        {
            try
            {
                var salesOrder = new Core.Domain.Sales.SalesOrderHeader()
                {
                    CustomerId = salesorderDto.CustomerId,
                    Date = salesorderDto.OrderDate,
                };

                foreach (var line in salesorderDto.SalesOrderLines)
                {
                    var salesOrderLine = new Core.Domain.Sales.SalesOrderLine();
                    salesOrderLine.Amount = line.Amount.GetValueOrDefault();
                    salesOrderLine.Discount = line.Discount.GetValueOrDefault();
                    salesOrderLine.Quantity = line.Quantity.GetValueOrDefault();
                    salesOrderLine.ItemId = line.ItemId.GetValueOrDefault();
                    salesOrderLine.MeasurementId = line.MeasurementId.GetValueOrDefault();

                    salesOrder.SalesOrderLines.Add(salesOrderLine);
                }

                _salesService.AddSalesOrder(salesOrder, true);

                salesorderDto.Id = salesOrder.Id;

                return new ObjectResult(salesorderDto);
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

            foreach (var quote in quotes)
            {
                var quoteDto = new Dto.Sales.SalesQuotation()
                {
                    Id = quote.Id,
                    No = quote.No,
                    CustomerId = quote.CustomerId,
                    CustomerName = quote.Customer.Party.Name,
                    PaymentTermId = quote.PaymentTermId,
                    QuotationDate = quote.Date,
                    ReferenceNo = quote.ReferenceNo,
                    SalesQuoteStatus = quote.Status.ToString(),
                    StatusId = (int)quote.Status
                };

                foreach (var line in quote.SalesQuoteLines)
                {
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

            return new ObjectResult(quoteDtos.OrderBy(q => q.Id).Reverse());
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Quotation(int id)
        {
            var quote = _salesService.GetSalesQuotationById(id);

            var quoteDto = new Dto.Sales.SalesQuotation()
            {
                Id = quote.Id,
                CustomerId = quote.CustomerId,
                CustomerName = quote.Customer.Party.Name,
                QuotationDate = quote.Date,
                PaymentTermId = quote.PaymentTermId,
                ReferenceNo = quote.ReferenceNo,
                StatusId = (int)quote.Status
            };


            foreach (var line in quote.SalesQuoteLines)
            {
                var lineDto = new Dto.Sales.SalesQuotationLine()
                {
                    Id = line.Id,
                    ItemId = line.ItemId,
                    MeasurementId = line.MeasurementId,
                    Quantity = line.Quantity,
                    Amount = line.Amount,
                    Discount = line.Discount
                };
                quoteDto.SalesQuotationLines.Add(lineDto);
            }

            return new ObjectResult(quoteDto);
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
                    No = salesInvoice.No,
                    CustomerId = salesInvoice.CustomerId,
                    CustomerName = salesInvoice.Customer.Party.Name,
                    InvoiceDate = salesInvoice.Date,
                    ReferenceNo = salesInvoice.ReferenceNo,
                    Posted = salesInvoice.GeneralLedgerHeaderId != null
                };

                foreach (var line in salesInvoice.SalesInvoiceLines)
                {
                    var lineDto = new Dto.Sales.SalesInvoiceLine()
                    {
                        ItemId = line.ItemId,
                        MeasurementId = line.MeasurementId,
                        Quantity = line.Quantity,
                        Amount = line.Amount,
                        Discount = line.Discount
                    };
                    salesInvoiceDto.SalesInvoiceLines.Add(lineDto);
                }

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
                        TotalAllocatedAmount = (decimal)invoice.CustomerAllocations.Sum(i => i.Amount),
                        Posted = invoice.GeneralLedgerHeaderId.HasValue
                    };

                    foreach (var line in invoice.SalesInvoiceLines)
                    {
                        var lineDto = new Dto.Sales.SalesInvoiceLine();
                        lineDto.Id = line.Id;
                        lineDto.Amount = line.Amount;
                        lineDto.Discount = line.Discount;
                        lineDto.Quantity = line.Quantity;
                        lineDto.ItemId = line.ItemId;
                        lineDto.MeasurementId = line.MeasurementId;

                        invoiceDto.SalesInvoiceLines.Add(lineDto);
                    }

                    invoicesDto.Add(invoiceDto);
                }

                return new ObjectResult(invoicesDto);
            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult(ex);
            }
        }

        [HttpPost]
        [Route("[action]")]
        public IActionResult SaveSalesOrder([FromBody]Dto.Sales.SalesOrder salesOrderDto)
        {
            string[] errors = null;
            try
            {
                if (!ModelState.IsValid)
                {
                    errors = new string[ModelState.ErrorCount];
                    foreach (var val in ModelState.Values)
                        for (int i = 0; i < ModelState.ErrorCount; i++)
                            errors[i] = val.Errors[i].ErrorMessage;

                    return new BadRequestObjectResult(errors);
                }

                bool isNew = salesOrderDto.Id == 0;
                Core.Domain.Sales.SalesOrderHeader salesOrder = null;

                if (isNew)
                {
                    salesOrder = new Core.Domain.Sales.SalesOrderHeader();
                    salesOrder.Status = SalesOrderStatus.Open;

                    if (salesOrderDto.QuotationId != null)
                    {
                        var quotation = _salesService.GetSalesQuotationById(salesOrderDto.QuotationId.Value);
                        quotation.Status = SalesQuoteStatus.ClosedOrderCreated;
                        _salesService.UpdateSalesQuote(quotation);
                    }

                }
                else
                {
                    salesOrder = _salesService.GetSalesOrderById(salesOrderDto.Id);
                }

                salesOrder.CustomerId = salesOrderDto.CustomerId;
                salesOrder.Date = salesOrderDto.OrderDate;
                salesOrder.PaymentTermId = salesOrderDto.PaymentTermId;
                salesOrder.ReferenceNo = salesOrderDto.ReferenceNo;

                foreach (var line in salesOrderDto.SalesOrderLines)
                {
                    if (!isNew)
                    {
                        var existingLine = salesOrder.SalesOrderLines.Where(id => id.Id == line.Id).FirstOrDefault();
                        if (salesOrder.SalesOrderLines.Where(id => id.Id == line.Id).FirstOrDefault() != null)
                        {
                            existingLine.Amount = line.Amount.GetValueOrDefault();
                            existingLine.Discount = line.Discount.GetValueOrDefault();
                            existingLine.Quantity = line.Quantity.GetValueOrDefault();
                            existingLine.ItemId = line.ItemId.GetValueOrDefault();
                            existingLine.MeasurementId = line.MeasurementId.GetValueOrDefault();
                        }
                        else
                        {
                            var salesOrderLine = new Core.Domain.Sales.SalesOrderLine();
                            salesOrderLine.Amount = line.Amount.GetValueOrDefault();
                            salesOrderLine.Discount = line.Discount.GetValueOrDefault();
                            salesOrderLine.Quantity = line.Quantity.GetValueOrDefault();
                            salesOrderLine.ItemId = line.ItemId.GetValueOrDefault();
                            salesOrderLine.MeasurementId = line.MeasurementId.GetValueOrDefault();

                            salesOrder.SalesOrderLines.Add(salesOrderLine);
                        }
                    }
                    else
                    {
                        var salesOrderLine = new Core.Domain.Sales.SalesOrderLine();
                        salesOrderLine.Amount = line.Amount.GetValueOrDefault();
                        salesOrderLine.Discount = line.Discount.GetValueOrDefault();
                        salesOrderLine.Quantity = line.Quantity.GetValueOrDefault();
                        salesOrderLine.ItemId = line.ItemId.GetValueOrDefault();
                        salesOrderLine.MeasurementId = line.MeasurementId.GetValueOrDefault();

                        salesOrder.SalesOrderLines.Add(salesOrderLine);
                    }
                }

                if (isNew)
                {
                    _salesService.AddSalesOrder(salesOrder, true);
                }
                else
                {
                    var deleted = (from line in salesOrder.SalesOrderLines
                                   where !salesOrderDto.SalesOrderLines.Any(x => x.Id == line.Id)
                                   select line).ToList();

                    foreach (var line in deleted)
                    {
                        if (line.SalesInvoiceLines.Count() > 0)
                            throw new Exception("The line cannot be deleted. An invoice line is created from the item.");
                    }

                    foreach (var line in deleted)
                    {
                        salesOrder.SalesOrderLines.Remove(line);
                    }

                    _salesService.UpdateSalesOrder(salesOrder);
                }


                return new OkObjectResult(Ok());
            }
            catch (Exception ex)
            {
                errors = new string[1] { ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }

        [HttpPost]
        [Route("[action]")]
        public IActionResult PostSalesInvoice([FromBody]Dto.Sales.SalesInvoice salesInvoiceDto)
        {
            string[] errors = null;

            try
            {
                if (!ModelState.IsValid)
                {
                    errors = new string[ModelState.ErrorCount];
                    foreach (var val in ModelState.Values)
                        for (int i = 0; i < ModelState.ErrorCount; i++)
                            errors[i] = val.Errors[i].ErrorMessage;

                    return new BadRequestObjectResult(errors);
                }

                _salesService.PostSalesInvoice(salesInvoiceDto.Id);

                return new ObjectResult(Ok());
            }
            catch (Exception ex)
            {
                errors = new string[1] { ex.InnerException != null ? ex.InnerException.Message : ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }

        [HttpPost]
        [Route("[action]")]
        public IActionResult SaveSalesInvoice([FromBody]Dto.Sales.SalesInvoice salesInvoiceDto)
        {
            string[] errors = null;

            try
            {
                if (!ModelState.IsValid)
                {
                    errors = new string[ModelState.ErrorCount];
                    foreach (var val in ModelState.Values)
                        for (int i = 0; i < ModelState.ErrorCount; i++)
                            errors[i] = val.Errors[i].ErrorMessage;

                    return new BadRequestObjectResult(errors);
                }

                bool isNew = salesInvoiceDto.Id == 0;
                Core.Domain.Sales.SalesInvoiceHeader salesInvoice = null;
                Core.Domain.Sales.SalesOrderHeader salesOrder = null;

                // Creating a new invoice
                if (isNew)
                {
                    // if fromsalesorderid has NO value, then create automatically a new sales order.
                    if (!salesInvoiceDto.FromSalesOrderId.HasValue)
                    {
                        salesOrder = new Core.Domain.Sales.SalesOrderHeader();
                        salesOrder.Date = salesInvoiceDto.InvoiceDate;
                        salesOrder.PaymentTermId = salesInvoiceDto.PaymentTermId;
                        salesOrder.CustomerId = salesInvoiceDto.CustomerId;
                        salesOrder.ReferenceNo = salesInvoiceDto.ReferenceNo;
                        salesOrder.Status = SalesOrderStatus.FullyInvoiced;
                    }
                    else
                    {
                        // else,  your invoice is created from existing (open) sales order.
                        salesOrder = _salesService.GetSalesOrderById(salesInvoiceDto.FromSalesOrderId.GetValueOrDefault());
                    }

                    // populate invoice header
                    salesInvoice = new Core.Domain.Sales.SalesInvoiceHeader();
                    salesInvoice.CustomerId = salesInvoiceDto.CustomerId.GetValueOrDefault();
                    salesInvoice.Date = salesInvoiceDto.InvoiceDate;
                    salesInvoice.PaymentTermId = salesInvoiceDto.PaymentTermId;
                    salesInvoice.ReferenceNo = salesInvoiceDto.ReferenceNo;

                    foreach (var line in salesInvoiceDto.SalesInvoiceLines)
                    {
                        var salesInvoiceLine = new Core.Domain.Sales.SalesInvoiceLine();

                        salesInvoiceLine.Amount = line.Amount.GetValueOrDefault();
                        salesInvoiceLine.Discount = line.Discount.GetValueOrDefault();
                        salesInvoiceLine.Quantity = line.Quantity.GetValueOrDefault();
                        salesInvoiceLine.ItemId = line.ItemId.GetValueOrDefault();
                        salesInvoiceLine.MeasurementId = line.MeasurementId.GetValueOrDefault();
                        salesInvoice.SalesInvoiceLines.Add(salesInvoiceLine);

                        // line.Id here is referring to SalesOrderLineId. It is pre-populated when you create a new sales invoice from sales order.
                        if (line.Id != 0)
                        {
                            salesInvoiceLine.SalesOrderLineId = line.Id;
                        }
                        else
                        {
                            // if you reach here, this line item is newly added to invoice which is not originally in sales order. create correspondin orderline and add to sales order.
                            var salesOrderLine = new Core.Domain.Sales.SalesOrderLine();
                            salesOrderLine.Amount = line.Amount.GetValueOrDefault();
                            salesOrderLine.Discount = line.Discount.GetValueOrDefault();
                            salesOrderLine.Quantity = line.Quantity.GetValueOrDefault();
                            salesOrderLine.ItemId = line.ItemId.GetValueOrDefault();
                            salesOrderLine.MeasurementId = line.MeasurementId.GetValueOrDefault();
                            salesInvoiceLine.SalesOrderLine = salesOrderLine;

                            salesOrder.SalesOrderLines.Add(salesOrderLine);

                            salesInvoiceLine.SalesOrderLine = salesOrderLine; // map invoice line to newly added orderline
                        }
                    }
                }
                else
                {
                    // if you reach here, you are updating existing invoice.
                    salesInvoice = _salesService.GetSalesInvoiceById(salesInvoiceDto.Id);

                    if (salesInvoice.GeneralLedgerHeaderId.HasValue)
                        throw new Exception("Invoice is already posted. Update is not allowed.");

                    salesInvoice.Date = salesInvoiceDto.InvoiceDate;
                    salesInvoice.PaymentTermId = salesInvoiceDto.PaymentTermId;
                    salesInvoice.ReferenceNo = salesInvoiceDto.ReferenceNo;

                    foreach (var line in salesInvoiceDto.SalesInvoiceLines)
                    {
                        var existingLine = salesInvoice.SalesInvoiceLines.Where(id => id.Id == line.Id).FirstOrDefault();
                        if (salesInvoice.SalesInvoiceLines.Where(id => id.Id == line.Id).FirstOrDefault() != null)
                        {
                            existingLine.Amount = line.Amount.GetValueOrDefault();
                            existingLine.Discount = line.Discount.GetValueOrDefault();
                            existingLine.Quantity = line.Quantity.GetValueOrDefault();
                            existingLine.ItemId = line.ItemId.GetValueOrDefault();
                            existingLine.MeasurementId = line.MeasurementId.GetValueOrDefault();
                        }
                        else
                        {
                            //if you reach here, this line item is newly added to invoice. also, it has no SalesOrderLineId.
                            var salesInvoiceLine = new Core.Domain.Sales.SalesInvoiceLine();
                            salesInvoiceLine.Amount = line.Amount.GetValueOrDefault();
                            salesInvoiceLine.Discount = line.Discount.GetValueOrDefault();
                            salesInvoiceLine.Quantity = line.Quantity.GetValueOrDefault();
                            salesInvoiceLine.ItemId = line.ItemId.GetValueOrDefault();
                            salesInvoiceLine.MeasurementId = line.MeasurementId.GetValueOrDefault();
                            salesInvoice.SalesInvoiceLines.Add(salesInvoiceLine);

                            // add a new order line.
                            var salesOrderLine = new Core.Domain.Sales.SalesOrderLine();
                            salesOrderLine.Amount = line.Amount.GetValueOrDefault();
                            salesOrderLine.Discount = line.Discount.GetValueOrDefault();
                            salesOrderLine.Quantity = line.Quantity.GetValueOrDefault();
                            salesOrderLine.ItemId = line.ItemId.GetValueOrDefault();
                            salesOrderLine.MeasurementId = line.MeasurementId.GetValueOrDefault();

                            // but on what order should the new orderline be added?
                            // note: each invoice is map to one and only one sales order. it can't be done that invoice lines came from multiple sales orders.
                            // with this rule, we are sure that all invoice lines are contained in the same sales order.
                            // therefore, we could just pick the first line, get the salesorderlineid, then get the salesorderheader.

                            // you will retrieve salesorder one time.
                            if (salesOrder == null)
                            {
                                // use the last value of existingLine
                                salesOrder = _salesService.GetSalesOrderLineById(existingLine.SalesOrderLine.SalesOrderHeaderId).SalesOrderHeader;
                                salesOrder.SalesOrderLines.Add(salesOrderLine);
                            }

                            salesInvoiceLine.SalesOrderLine = salesOrderLine; // map invoice line to newly added orderline
                        }
                    }
                }

                if (!isNew)
                {
                    var deleted = (from line in salesInvoice.SalesInvoiceLines
                                   where !salesInvoiceDto.SalesInvoiceLines.Any(x => x.Id == line.Id)
                                   select line).ToList();

                    foreach (var line in deleted)
                    {
                        salesInvoice.SalesInvoiceLines.Remove(line);
                    }
                }

                _salesService.SaveSalesInvoice(salesInvoice, salesOrder);

                return new OkObjectResult(Ok());
            }
            catch (Exception ex)
            {
                errors = new string[1] { ex.InnerException != null ? ex.InnerException.Message : ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }

        [HttpPost]
        [Route("[action]")]
        public IActionResult SaveQuotation([FromBody]Dto.Sales.SalesQuotation quotationDto)
        {
            string[] errors = null;

            try
            {
                if (!ModelState.IsValid)
                {
                    errors = new string[ModelState.ErrorCount];
                    foreach (var val in ModelState.Values)
                        for (int i = 0; i < ModelState.ErrorCount; i++)
                            errors[i] = val.Errors[i].ErrorMessage;

                    return new BadRequestObjectResult(errors);
                }

                bool isNew = quotationDto.Id == 0;
                Core.Domain.Sales.SalesQuoteHeader salesQuote = null;

                if (isNew)
                {
                    salesQuote = new Core.Domain.Sales.SalesQuoteHeader();
                    salesQuote.Status = SalesQuoteStatus.Draft;
                }
                else
                {
                    salesQuote = _salesService.GetSalesQuotationById(quotationDto.Id);
                    salesQuote.Status = (SalesQuoteStatus)quotationDto.StatusId;
                }

                salesQuote.CustomerId = quotationDto.CustomerId.GetValueOrDefault();
                salesQuote.Date = quotationDto.QuotationDate;

                salesQuote.ReferenceNo = quotationDto.ReferenceNo;
                salesQuote.PaymentTermId = quotationDto.PaymentTermId;
                
                foreach (var line in quotationDto.SalesQuotationLines)
                {
                    if (!isNew)
                    {
                        var existingLine = salesQuote.SalesQuoteLines.Where(id => id.Id == line.Id).FirstOrDefault();
                        if (salesQuote.SalesQuoteLines.Where(id => id.Id == line.Id).FirstOrDefault() != null)
                        {
                            existingLine.Amount = line.Amount == null ? 0 : line.Amount.Value;
                            existingLine.Discount = line.Discount == null ? 0 : line.Discount.Value;
                            existingLine.Quantity = line.Quantity == null ? 0 : line.Quantity.Value;
                            existingLine.ItemId = line.ItemId.GetValueOrDefault();
                            existingLine.MeasurementId = line.MeasurementId.GetValueOrDefault();
                        }
                        else
                        {
                            var salesQuoteLine = new Core.Domain.Sales.SalesQuoteLine();
                            salesQuoteLine.Amount = line.Amount == null ? 0 : line.Amount.Value;
                            salesQuoteLine.Discount = line.Discount == null ? 0 : line.Discount.Value;
                            salesQuoteLine.Quantity = line.Quantity == null ? 0 : line.Quantity.Value;
                            salesQuoteLine.ItemId = line.ItemId.GetValueOrDefault();
                            salesQuoteLine.MeasurementId = line.MeasurementId.GetValueOrDefault();

                            salesQuote.SalesQuoteLines.Add(salesQuoteLine);
                        }
                    }
                    else
                    {
                        var salesQuoteLine = new Core.Domain.Sales.SalesQuoteLine();
                        salesQuoteLine.Amount = line.Amount == null ? 0 : line.Amount.Value;
                        salesQuoteLine.Discount = line.Discount == null ? 0 : line.Discount.Value;
                        salesQuoteLine.Quantity = line.Quantity == null ? 0 : line.Quantity.Value;
                        salesQuoteLine.ItemId = line.ItemId.GetValueOrDefault();
                        salesQuoteLine.MeasurementId = line.MeasurementId.GetValueOrDefault();

                        salesQuote.SalesQuoteLines.Add(salesQuoteLine);
                    }
                }

                if (isNew)
                {
                    _salesService.AddSalesQuote(salesQuote);
                }
                else
                {
                    var deleted = (from line in salesQuote.SalesQuoteLines
                                   where !quotationDto.SalesQuotationLines.Any(x => x.Id == line.Id)
                                   select line).ToList();

                    foreach (var line in deleted)
                    {
                        salesQuote.SalesQuoteLines.Remove(line);
                    }

                    _salesService.UpdateSalesQuote(salesQuote);
                }


                return new OkObjectResult(Ok());
            }
            catch (Exception ex)
            {
                errors = new string[1] { ex.InnerException != null ? ex.InnerException.Message : ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }

        [HttpPost]
        [Route("[action]")]
        public IActionResult SaveReceipt([FromBody]dynamic receiptDto)
        {
            string[] errors = null;

            try
            {
                if (!ModelState.IsValid)
                {
                    errors = new string[ModelState.ErrorCount];
                    foreach (var val in ModelState.Values)
                        for (int i = 0; i < ModelState.ErrorCount; i++)
                            errors[i] = val.Errors[i].ErrorMessage;

                    return new BadRequestObjectResult(errors);
                }

                var bank = _financialService.GetCashAndBanks().Where(id => id.Id == (int)receiptDto.AccountToDebitId).FirstOrDefault();

                var salesReceipt = new Core.Domain.Sales.SalesReceiptHeader();
                salesReceipt.Date = receiptDto.ReceiptDate;
                salesReceipt.CustomerId = receiptDto.CustomerId;
                salesReceipt.AccountToDebitId = bank.AccountId;
                salesReceipt.Amount = receiptDto.Amount;

                var customer = _salesService.GetCustomerById((int)receiptDto.CustomerId);
                if (customer.CustomerAdvancesAccountId != (int)receiptDto.AccountToCreditId)
                    throw new Exception("Invalid account.");

                var salesReceiptLine = new Core.Domain.Sales.SalesReceiptLine();
                salesReceiptLine.AccountToCreditId = receiptDto.AccountToCreditId;
                salesReceiptLine.AmountPaid = receiptDto.Amount;
                salesReceiptLine.Amount = receiptDto.Amount;
                salesReceiptLine.Quantity = 1;

                salesReceipt.SalesReceiptLines.Add(salesReceiptLine);

                _salesService.AddSalesReceiptNoInvoice(salesReceipt);

                return new ObjectResult(Ok());
            }
            catch (Exception ex)
            {
                errors = new string[1] { ex.InnerException != null ? ex.InnerException.Message : ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }

        [HttpPost]
        [Route("[action]")]
        public IActionResult SaveAllocation([FromBody]dynamic allocationDto)
        {
            string[] errors = null;

            try
            {
                if (!ModelState.IsValid)
                {
                    errors = new string[ModelState.ErrorCount];
                    foreach (var val in ModelState.Values)
                        for (int i = 0; i < ModelState.ErrorCount; i++)
                            errors[i] = val.Errors[i].ErrorMessage;
                    return new BadRequestObjectResult(errors);
                }

                foreach (var line in allocationDto.AllocationLines)
                {
                    decimal? amount = (decimal?)line.AmountToAllocate;
                    if (amount.HasValue)
                    {
                        var allocation = new Core.Domain.Sales.CustomerAllocation();
                        allocation.CustomerId = allocationDto.CustomerId;
                        allocation.Date = allocationDto.Date;
                        allocation.SalesInvoiceHeaderId = line.InvoiceId;
                        allocation.SalesReceiptHeaderId = allocationDto.ReceiptId;
                        allocation.Amount = amount.GetValueOrDefault();

                        _salesService.SaveCustomerAllocation(allocation);
                    }
                }

                return new ObjectResult(Ok());
            }
            catch (Exception ex)
            {
                errors = new string[1] { ex.InnerException != null ? ex.InnerException.Message : ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult GetMonthlySales()
        {
            var salesOrders = _salesService.GetSalesInvoices().Where(a => a.GeneralLedgerHeaderId != null);

            IList<Dto.Sales.MonthlySales> monthlySalesDto = new List<Dto.Sales.MonthlySales>();

            IList<Dto.Sales.MonthlySales> finalmonthlySalesDto = new List<Dto.Sales.MonthlySales>();

            foreach (var item in salesOrders)
            {
                foreach (var line in item.SalesInvoiceLines)
                {
                    var dtoSales = new MonthlySales();
                    dtoSales.Month = item.Date.Month.ToString();
                    dtoSales.Amount = line.Amount * line.Quantity;
                    monthlySalesDto.Add(dtoSales);
                }
            }

            var totalSales = monthlySalesDto.ToList().GroupBy(a => a.Month)
            .Select(ms => new MonthlySales
            {
                Month = ms.First().Month,
                Amount = ms.Sum(x => x.Amount),
            }).ToList();

            for (int i = 1; i <= DateTime.Now.Month; i++)
            {
                var sales = new MonthlySales();
                var month = i + "/1/" + DateTime.Now.Year;
                sales.Month = Convert.ToDateTime(month).ToString("MMMMM");
                sales.Amount = totalSales.Where(a => a.Month == i.ToString()).Select(x => x.Amount).FirstOrDefault();
                finalmonthlySalesDto.Add(sales);
            }

            return Json(finalmonthlySalesDto);
        }







        [HttpGet]
        [Route("[action]")]
        public IActionResult SalesInvoiceForPrinting(int id)
        {
            try
            {
                var salesInvoice = _salesService.GetSalesInvoiceById(id);

                //var items = _salesService.Ge
                var salesInvoiceDto = new Dto.Sales.SalesInvoice()
                {
                    Id = salesInvoice.Id,
                    CustomerId = salesInvoice.CustomerId,
                    CustomerName = salesInvoice.Customer.Party.Name,
                    CustomerEmail = salesInvoice.Customer.Party.Email,
                    InvoiceDate = salesInvoice.Date,
                    SalesInvoiceLines = new List<Dto.Sales.SalesInvoiceLine>(),
                    PaymentTermId = salesInvoice.PaymentTermId,
                    ReferenceNo = salesInvoice.ReferenceNo,
                    Posted = salesInvoice.GeneralLedgerHeaderId != null,
                    CompanyName = _adminService.GetDefaultCompany().Name

                };

                decimal? totalTax = 0;
                foreach (var line in salesInvoice.SalesInvoiceLines)
                {
                    var lineDto = new Dto.Sales.SalesInvoiceLine();

                    lineDto.Id = line.Id;
                    lineDto.Amount = line.Amount;
                    lineDto.Discount = line.Discount;
                    lineDto.Quantity = line.Quantity;
                    lineDto.ItemId = line.ItemId;
                    lineDto.MeasurementId = line.MeasurementId;

                    lineDto.ItemDescription = _inventoryService.GetItemById(line.ItemId).Description;
                    lineDto.MeasurementDescription = _inventoryService.GetMeasurementById(line.MeasurementId).Description;

                    if (_taxService != null)
                    {
                        var taxes = _taxService.GetIntersectionTaxes(line.ItemId, salesInvoice.CustomerId,
                            salesInvoice.Customer.Party.PartyType);

                        totalTax += _taxService.GetSalesLineTaxAmount(line.Quantity, line.Amount, line.Discount, taxes);

                    }
                    salesInvoiceDto.SalesInvoiceLines.Add(lineDto);
                }
                salesInvoiceDto.TotalTax = totalTax;
                salesInvoiceDto.TotalAmountAfterTax = (salesInvoiceDto.Amount + salesInvoiceDto.TotalTax);


                return new ObjectResult(salesInvoiceDto);
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex);
            }
        }

        [HttpPost]
        [Route("[action]")]
        public IActionResult BookQuotation(int id)
        {

            try
            {
                _salesService.BookQuotation(id);
                return new ObjectResult(Ok());
            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult(ex.Message);
            }


        }
    }
}