using Api.ActionFilters;
using Api.Filters;
using Core.Domain;
using Dto.Sales;
using Microsoft.AspNetCore.Mvc;
using Services.Administration;
using Services.Financial;
using Services.Inventory;
using Services.Sales;
using Services.TaxSystem;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    [RequireModule("sales")]
    public class SalesController : BaseController
    {
        private readonly IAdministrationService _adminService;
        private readonly ISalesService _salesService;
        private readonly IFinancialService _financialService;
        private readonly IInventoryService _inventoryService;
        private readonly ITaxService _taxService;
        private readonly ILogger<SalesController> _logger;

        public SalesController(IAdministrationService adminService,
            ISalesService salesService,
            IFinancialService financialService,
            IInventoryService inventoryService,
            ITaxService taxService,
            ILogger<SalesController> logger)
        {
            _adminService = adminService;
            _salesService = salesService;
            _financialService = financialService;
            _inventoryService = inventoryService;
            _taxService = taxService;
            _logger = logger;
        }

        [HttpPost]
        [Route("SaveCustomer")]
        public IActionResult SaveCustomer([FromBody] Dto.Sales.Customer customerDto)
        {
            bool isNew = customerDto.Id == 0;
            Core.Domain.Sales.Customer? customer = null;

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
            customer.Party.CompanyStreet = customerDto.CompanyAddress?.Street1;
            customer.Party.CompanyProvince = customerDto.CompanyAddress?.Province;
            customer.Party.CompanyPostalCode = customerDto.CompanyAddress?.PostalCode;
            customer.Party.CompanyCountry = customerDto.CompanyAddress?.Country;
            customer.Party.ShippingStreet = customerDto.ShippingAddress?.Street1;
            customer.Party.ShippingProvince = customerDto.ShippingAddress?.Province;
            customer.Party.ShippingPostalCode = customerDto.ShippingAddress?.PostalCode;
            customer.Party.ShippingCountry = customerDto.ShippingAddress?.Country;

            var firstName = customerDto.PrimaryContact?.FirstName;
            var lastName = customerDto.PrimaryContact?.LastName;
            if (string.IsNullOrWhiteSpace(firstName))
            {
                var nameParts = (customerDto.Name ?? "Customer Contact").Trim().Split(' ', 2);
                firstName = nameParts.FirstOrDefault() ?? "Customer";
                lastName = nameParts.Length > 1 ? nameParts[1] : "Contact";
            }
            customer.PrimaryContact.FirstName = firstName;
            customer.PrimaryContact.LastName = lastName ?? "Contact";
            customer.PrimaryContact.Party.Name = $"{customer.PrimaryContact.FirstName} {customer.PrimaryContact.LastName}".Trim();
            customer.PrimaryContact.Party.Phone = customerDto.PrimaryContact?.Party?.Phone ?? customerDto.Phone;
            customer.PrimaryContact.Party.Email = customerDto.PrimaryContact?.Party?.Email ?? customerDto.Email;
            customer.PrimaryContact.Party.Fax = customerDto.PrimaryContact?.Party?.Fax ?? customerDto.Fax;
            customer.PrimaryContact.Party.Website = customerDto.PrimaryContact?.Party?.Website ?? customerDto.Website;

            // Safe auto-assign default accounts for Invoicing continuity if omitted
            var accounts = _financialService.GetAccounts().ToList();
            var accountAR = accounts.FirstOrDefault(a => a.AccountCode == "10120") ?? accounts.FirstOrDefault(a => a.AccountClassId == 1);
            var accountSales = accounts.FirstOrDefault(a => a.AccountCode == "40100") ?? accounts.FirstOrDefault(a => a.AccountClassId == 4);
            var accountAdvances = accounts.FirstOrDefault(a => a.AccountCode == "20120") ?? accounts.FirstOrDefault(a => a.AccountClassId == 2);
            var accountDiscount = accounts.FirstOrDefault(a => a.AccountCode == "40400");

            customer.AccountsReceivableAccountId = (customerDto.AccountsReceivableId.HasValue && customerDto.AccountsReceivableId.Value > 0)
                ? customerDto.AccountsReceivableId.Value
                : (customer.AccountsReceivableAccountId ?? accountAR?.Id);

            customer.SalesAccountId = (customerDto.SalesAccountId.HasValue && customerDto.SalesAccountId.Value > 0)
                ? customerDto.SalesAccountId.Value
                : (customer.SalesAccountId ?? accountSales?.Id);

            customer.CustomerAdvancesAccountId = (customerDto.PrepaymentAccountId.HasValue && customerDto.PrepaymentAccountId.Value > 0)
                ? customerDto.PrepaymentAccountId.Value
                : (customer.CustomerAdvancesAccountId ?? accountAdvances?.Id);

            customer.SalesDiscountAccountId = (customerDto.SalesDiscountAccountId.HasValue && customerDto.SalesDiscountAccountId.Value > 0)
                ? customerDto.SalesDiscountAccountId.Value
                : (customer.SalesDiscountAccountId ?? accountDiscount?.Id);

            var defaultPaymentTerm = _adminService.GetPaymentTerms().FirstOrDefault();
            customer.PaymentTermId = (customerDto.PaymentTermId.HasValue && customerDto.PaymentTermId.Value > 0)
                ? customerDto.PaymentTermId.Value
                : (customer.PaymentTermId ?? defaultPaymentTerm?.Id);

            customer.TaxGroupId = (customerDto.TaxGroupId.HasValue && customerDto.TaxGroupId.Value > 0)
                ? customerDto.TaxGroupId.Value
                : customer.TaxGroupId;

            customer.ModifiedBy = GetUserNameFromRequestHeader();

            if (isNew)
                _salesService.AddCustomer(customer);
            else
                _salesService.UpdateCustomer(customer);

            return Ok(new { id = customer.Id, no = customer.No, name = customer.Party.Name });
        }

        [HttpGet]
        [Route("Customer")]
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
                customerDto.CompanyAddress = new Dto.Common.Address
                {
                    Street1 = customer.Party.CompanyStreet,
                    Province = customer.Party.CompanyProvince,
                    PostalCode = customer.Party.CompanyPostalCode,
                    Country = customer.Party.CompanyCountry
                };
                customerDto.ShippingAddress = new Dto.Common.Address
                {
                    Street1 = customer.Party.ShippingStreet,
                    Province = customer.Party.ShippingProvince,
                    PostalCode = customer.Party.ShippingPostalCode,
                    Country = customer.Party.ShippingCountry
                };

                if (customer.PrimaryContact != null)
                {
                    customerDto.PrimaryContact!.FirstName = customer.PrimaryContact.FirstName;
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
        [Route("Customers")] // api/Sales/Customers
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
                    customerDto.Contact = customer.PrimaryContact != null ? $"{customer.PrimaryContact.FirstName} {customer.PrimaryContact.LastName}".Trim() : string.Empty;
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
        [Route("SalesOrders")] // api/Sales/SalesOrders
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
                        CustomerId = salesOrder.CustomerId!.Value,
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
        [Route("SalesOrder")]
        public IActionResult SalesOrder(int id)
        {
            try
            {
                var salesOrder = _salesService.GetSalesOrderById(id);

                if (salesOrder == null)
                {
                    return new NotFoundObjectResult(new { error = "Sales order not found." });
                }

                var salesOrderDto = new Dto.Sales.SalesOrder()
                {
                    Id = salesOrder.Id,
                    CustomerId = salesOrder.CustomerId ?? 0,
                    CustomerNo = salesOrder.Customer?.No ?? "N/A",
                    CustomerName = salesOrder.Customer?.Party?.Name ?? "Unknown",
                    OrderDate = salesOrder.Date,
                    PaymentTermId = salesOrder.PaymentTermId ?? 0,
                    ReferenceNo = salesOrder.ReferenceNo,
                    StatusId = salesOrder.Status.HasValue ? (int)salesOrder.Status.Value : 0,
                    SalesOrderLines = new List<Dto.Sales.SalesOrderLine>()
                };

                if (salesOrder.SalesOrderLines != null)
                {
                    foreach (var line in salesOrder.SalesOrderLines)
                    {
                        var lineDto = new Dto.Sales.SalesOrderLine();
                        lineDto.Id = line.Id;
                        lineDto.Amount = line.Amount;
                        lineDto.Discount = line.Discount;
                        lineDto.Quantity = line.Quantity;
                        lineDto.ItemId = line.ItemId;
                        lineDto.ItemDescription = line.Item?.Description ?? "Unknown";
                        lineDto.MeasurementId = line.MeasurementId;
                        lineDto.MeasurementDescription = line.Measurement?.Description ?? "Unknown";
                        lineDto.RemainingQtyToInvoice = line.GetRemainingQtyToInvoice();

                        salesOrderDto.SalesOrderLines.Add(lineDto);
                    }
                }

                return new ObjectResult(salesOrderDto);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.ToString());
                return new BadRequestObjectResult(new { error = "An error occurred while loading the sales order.", details = ex.Message });
            }
        }

        [HttpGet]
        [Route("SalesInvoice")]
        public IActionResult SalesInvoice(int id)
        {
            try
            {
                var salesInvoice = _salesService.GetSalesInvoiceById(id);
                if (salesInvoice == null)
                    return NotFound(new { message = $"Invoice #{id} not found." });

                var paymentTerm = _adminService.GetPaymentTerms().FirstOrDefault(t => t.Id == salesInvoice.PaymentTermId);
                var dueDays = paymentTerm?.DueAfterDays ?? 30;
                var dueDate = salesInvoice.Date.AddDays(dueDays);

                var defaultCompany = _adminService.GetDefaultCompany();

                var party = salesInvoice.Customer?.Party;
                var addressParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(party?.CompanyStreet)) addressParts.Add(party.CompanyStreet);
                if (!string.IsNullOrWhiteSpace(party?.CompanyProvince)) addressParts.Add(party.CompanyProvince);
                if (!string.IsNullOrWhiteSpace(party?.CompanyPostalCode)) addressParts.Add(party.CompanyPostalCode);
                if (!string.IsNullOrWhiteSpace(party?.CompanyCountry)) addressParts.Add(party.CompanyCountry);
                var customerAddress = addressParts.Count > 0 ? string.Join(", ", addressParts) : null;

                var salesInvoiceDto = new Dto.Sales.SalesInvoice()
                {
                    Id = salesInvoice.Id,
                    CustomerId = salesInvoice.CustomerId,
                    CustomerName = party?.Name ?? "Unknown",
                    CustomerEmail = party?.Email,
                    CustomerPhone = party?.Phone,
                    CustomerAddress = customerAddress,
                    No = salesInvoice.No,
                    InvoiceDate = salesInvoice.Date,
                    DueDate = dueDate,
                    SalesInvoiceLines = new List<Dto.Sales.SalesInvoiceLine>(),
                    PaymentTermId = salesInvoice.PaymentTermId,
                    ReferenceNo = salesInvoice.ReferenceNo,
                    Posted = salesInvoice.GeneralLedgerHeaderId != null,
                    CompanyName = defaultCompany?.Name
                };

                if (salesInvoice.CustomerAllocations != null)
                {
                    salesInvoiceDto.Payments = salesInvoice.CustomerAllocations
                        .OrderByDescending(a => a.Date)
                        .Select(a => new Dto.Sales.InvoicePaymentItemDto
                        {
                            Id = a.Id,
                            ReceiptNo = a.SalesReceiptHeader?.No,
                            Date = a.Date,
                            Amount = a.Amount,
                            PaymentMethod = a.SalesReceiptHeader?.PaymentMethod ?? "Payment",
                            ReferenceNo = a.SalesReceiptHeader?.ReferenceNo,
                            Notes = a.SalesReceiptHeader?.Memo
                        }).ToList();
                }

                decimal totalTax = 0;
                foreach (var line in salesInvoice.SalesInvoiceLines)
                {
                    var lineDto = new Dto.Sales.SalesInvoiceLine
                    {
                        Id = line.Id,
                        Amount = line.Amount,
                        Discount = line.Discount,
                        Quantity = line.Quantity,
                        ItemId = line.ItemId,
                        MeasurementId = line.MeasurementId,
                        ItemDescription = line.Item?.Description ?? "Item",
                        MeasurementDescription = line.Measurement?.Description ?? "EA"
                    };

                    decimal lineAmount = (line.Quantity * line.Amount) - ((line.Discount / 100m) * (line.Quantity * line.Amount));
                    lineDto.LineTotal = lineAmount;

                    if (_taxService != null && line.ItemId > 0 && salesInvoice.Customer?.Party != null)
                    {
                        try
                        {
                            var taxes = _taxService.GetIntersectionTaxes(line.ItemId, salesInvoice.CustomerId, salesInvoice.Customer.Party.PartyType);
                            var taxAmount = _taxService.GetSalesLineTaxAmount(line.Quantity, line.Amount, line.Discount, taxes);
                            lineDto.TaxAmount = taxAmount;
                            totalTax += taxAmount;
                        }
                        catch { /* Ignore tax calc errors */ }
                    }

                    salesInvoiceDto.SalesInvoiceLines.Add(lineDto);
                }

                salesInvoiceDto.TotalTax = totalTax;
                decimal grandTotal = salesInvoiceDto.Amount + totalTax + salesInvoice.ShippingHandlingCharge;
                salesInvoiceDto.TotalAmountAfterTax = grandTotal;

                decimal allocated = salesInvoice.CustomerAllocations != null ? salesInvoice.CustomerAllocations.Sum(a => a.Amount) : 0;
                salesInvoiceDto.TotalAllocatedAmount = allocated;
                salesInvoiceDto.PaidAmount = allocated;
                salesInvoiceDto.BalanceDue = Math.Max(0, grandTotal - allocated);

                // Compute calculated status
                if (salesInvoice.Status == Core.Domain.SalesInvoiceStatus.Void)
                {
                    salesInvoiceDto.Status = "Void";
                    salesInvoiceDto.StatusId = (int)Core.Domain.SalesInvoiceStatus.Void;
                }
                else if (salesInvoice.Status == Core.Domain.SalesInvoiceStatus.Draft || (!salesInvoiceDto.Posted && salesInvoice.Status != Core.Domain.SalesInvoiceStatus.Open))
                {
                    salesInvoiceDto.Status = "Draft";
                    salesInvoiceDto.StatusId = (int)Core.Domain.SalesInvoiceStatus.Draft;
                }
                else if (salesInvoiceDto.BalanceDue <= 0 && allocated > 0)
                {
                    salesInvoiceDto.Status = "Paid";
                    salesInvoiceDto.StatusId = (int)Core.Domain.SalesInvoiceStatus.Closed;
                }
                else if (allocated > 0 && salesInvoiceDto.BalanceDue > 0)
                {
                    salesInvoiceDto.Status = "Partially Paid";
                    salesInvoiceDto.StatusId = (int)Core.Domain.SalesInvoiceStatus.Open;
                }
                else if (dueDate.Date < DateTime.UtcNow.Date)
                {
                    salesInvoiceDto.Status = "Overdue";
                    salesInvoiceDto.StatusId = (int)Core.Domain.SalesInvoiceStatus.Overdue;
                }
                else
                {
                    salesInvoiceDto.Status = "Open";
                    salesInvoiceDto.StatusId = (int)Core.Domain.SalesInvoiceStatus.Open;
                }

                // is this journal entry ready for posting?
                if (!salesInvoiceDto.Posted && salesInvoiceDto.SalesInvoiceLines.Count >= 1)
                {
                    salesInvoiceDto.ReadyForPosting = true;
                }

                return new ObjectResult(salesInvoiceDto);
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex);
            }
        }

        [HttpPost]
        [Route("addsalesorder")]
        public IActionResult AddSalesOrder([FromBody] Dto.Sales.SalesOrder salesorderDto)
        {
            try
            {
                var salesOrder = new Core.Domain.Sales.SalesOrderHeader()
                {
                    CustomerId = salesorderDto.CustomerId,
                    Date = salesorderDto.OrderDate,
                    PaymentTermId = salesorderDto.PaymentTermId,
                    ReferenceNo = salesorderDto.ReferenceNo
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
        [Route("Quotations")] // api/Sales/Quotations
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
                    StatusId = (int)quote.Status!
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
        [Route("Quotation")]
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
                StatusId = (int)quote.Status!
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
                    Discount = line.Discount,
                    ItemDescription = line.Item.Description,
                    MeasurementDescription = line.Measurement.Description
                };

                _logger.LogInformation("Quotation line: " + lineDto.ItemDescription);

                quoteDto.SalesQuotationLines.Add(lineDto);
            }

            return new ObjectResult(quoteDto);
        }

        [HttpGet]
        [Route("SalesInvoices")] // api/Sales/SalesInvoices
        public IActionResult SalesInvoices()
        {
            var salesInvoices = _salesService.GetSalesInvoices().OrderByDescending(i => i.Date).ThenByDescending(i => i.Id);
            IList<Dto.Sales.SalesInvoice> salesInvoicesDto = new List<Dto.Sales.SalesInvoice>();
            var paymentTerms = _adminService.GetPaymentTerms();

            foreach (var salesInvoice in salesInvoices)
            {
                var term = paymentTerms.FirstOrDefault(t => t.Id == salesInvoice.PaymentTermId);
                var dueDays = term?.DueAfterDays ?? 30;
                var dueDate = salesInvoice.Date.AddDays(dueDays);

                decimal allocatedAmount = salesInvoice.CustomerAllocations != null 
                    ? salesInvoice.CustomerAllocations.Sum(i => i.Amount) 
                    : 0;

                var salesInvoiceDto = new Dto.Sales.SalesInvoice()
                {
                    Id = salesInvoice.Id,
                    No = salesInvoice.No,
                    CustomerId = salesInvoice.CustomerId,
                    CustomerName = salesInvoice.Customer?.Party?.Name ?? "Unknown",
                    CustomerEmail = salesInvoice.Customer?.Party?.Email,
                    InvoiceDate = salesInvoice.Date,
                    DueDate = dueDate,
                    PaymentTermId = salesInvoice.PaymentTermId,
                    ReferenceNo = salesInvoice.ReferenceNo,
                    TotalAllocatedAmount = allocatedAmount,
                    PaidAmount = allocatedAmount,
                    Posted = salesInvoice.GeneralLedgerHeaderId != null
                };

                decimal totalTax = 0;
                // Populate line items - Amount will be calculated automatically by the DTO
                foreach (var line in salesInvoice.SalesInvoiceLines)
                {
                    var lineDto = new Dto.Sales.SalesInvoiceLine()
                    {
                        Id = line.Id,
                        ItemId = line.ItemId,
                        MeasurementId = line.MeasurementId,
                        Quantity = line.Quantity,
                        Amount = line.Amount,
                        Discount = line.Discount,
                        ItemDescription = line.Item?.Description ?? "Item",
                        MeasurementDescription = line.Measurement?.Description ?? "EA"
                    };

                    decimal lineAmount = (line.Quantity * line.Amount) - ((line.Discount / 100m) * (line.Quantity * line.Amount));
                    lineDto.LineTotal = lineAmount;

                    if (_taxService != null && line.ItemId > 0 && salesInvoice.Customer?.Party != null)
                    {
                        try
                        {
                            var taxes = _taxService.GetIntersectionTaxes(line.ItemId, salesInvoice.CustomerId, salesInvoice.Customer.Party.PartyType);
                            var taxAmount = _taxService.GetSalesLineTaxAmount(line.Quantity, line.Amount, line.Discount, taxes);
                            lineDto.TaxAmount = taxAmount;
                            totalTax += taxAmount;
                        }
                        catch { /* Ignore tax calc errors on listing */ }
                    }

                    salesInvoiceDto.SalesInvoiceLines!.Add(lineDto);
                }

                salesInvoiceDto.TotalTax = totalTax;
                decimal grandTotal = salesInvoiceDto.Amount + totalTax + salesInvoice.ShippingHandlingCharge;
                salesInvoiceDto.TotalAmountAfterTax = grandTotal;
                salesInvoiceDto.BalanceDue = Math.Max(0, grandTotal - allocatedAmount);

                // Compute calculated status
                if (salesInvoice.Status == Core.Domain.SalesInvoiceStatus.Void)
                {
                    salesInvoiceDto.Status = "Void";
                    salesInvoiceDto.StatusId = (int)Core.Domain.SalesInvoiceStatus.Void;
                }
                else if (salesInvoice.Status == Core.Domain.SalesInvoiceStatus.Draft || (!salesInvoiceDto.Posted && salesInvoice.Status != Core.Domain.SalesInvoiceStatus.Open))
                {
                    salesInvoiceDto.Status = "Draft";
                    salesInvoiceDto.StatusId = (int)Core.Domain.SalesInvoiceStatus.Draft;
                }
                else if (salesInvoiceDto.BalanceDue <= 0 && allocatedAmount > 0)
                {
                    salesInvoiceDto.Status = "Paid";
                    salesInvoiceDto.StatusId = (int)Core.Domain.SalesInvoiceStatus.Closed;
                }
                else if (allocatedAmount > 0 && salesInvoiceDto.BalanceDue > 0)
                {
                    salesInvoiceDto.Status = "Partially Paid";
                    salesInvoiceDto.StatusId = (int)Core.Domain.SalesInvoiceStatus.Open;
                }
                else if (dueDate.Date < DateTime.UtcNow.Date)
                {
                    salesInvoiceDto.Status = "Overdue";
                    salesInvoiceDto.StatusId = (int)Core.Domain.SalesInvoiceStatus.Overdue;
                }
                else
                {
                    salesInvoiceDto.Status = "Open";
                    salesInvoiceDto.StatusId = (int)Core.Domain.SalesInvoiceStatus.Open;
                }

                salesInvoicesDto.Add(salesInvoiceDto);
            }

            return new ObjectResult(salesInvoicesDto);
        }

        [HttpGet]
        [Route("SalesReceipts")] // api/Sales/SalesReceipts
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
                    CustomerName = salesReceipt.Customer?.Party.Name,
                    ReceiptDate = salesReceipt.Date,
                    Amount = salesReceipt.Amount,
                    RemainingAmountToAllocate = salesReceipt.AvailableAmountToAllocate,
                    PaymentMethod = salesReceipt.PaymentMethod,
                    ReferenceNo = salesReceipt.ReferenceNo,
                    Memo = salesReceipt.Memo
                };

                salesReceiptsDto.Add(salesReceiptDto);
            }

            return new ObjectResult(salesReceiptsDto);
        }

        [HttpGet]
        [Route("SalesReceipt")]
        public IActionResult SalesReceipt(int id)
        {
            var salesReceipt = _salesService.GetSalesReceiptById(id);
            var creditAccountId = salesReceipt.SalesReceiptLines.FirstOrDefault()?.AccountToCreditId ?? 0;
            var debitCashBankId = _financialService.GetCashAndBanks().Where(cb => cb.AccountId == salesReceipt.AccountToDebitId).FirstOrDefault()?.Id ?? 0;

            var salesReceiptDto = new Dto.Sales.SalesReceipt()
            {
                Id = salesReceipt.Id,
                ReceiptNo = salesReceipt.No,
                CustomerId = salesReceipt.CustomerId,
                CustomerName = salesReceipt.Customer?.Party.Name,
                ReceiptDate = salesReceipt.Date,
                Amount = salesReceipt.Amount,
                RemainingAmountToAllocate = salesReceipt.AvailableAmountToAllocate,
                AccountToDebitId = debitCashBankId,
                AccountToCreditId = creditAccountId,
                PaymentMethod = salesReceipt.PaymentMethod,
                ReferenceNo = salesReceipt.ReferenceNo,
                Memo = salesReceipt.Memo
            };

            return new ObjectResult(salesReceiptDto);
        }

        [HttpGet]
        [Route("CustomerInvoices")] // api/Sales/CustomerInvoices
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

                        invoiceDto.SalesInvoiceLines!.Add(lineDto);
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
        [Route("SaveSalesOrder")]
        public IActionResult SaveSalesOrder([FromBody] Dto.Sales.SalesOrder salesOrderDto)
        {
            string[]? errors = null;
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
                Core.Domain.Sales.SalesOrderHeader? salesOrder = null;

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
                            existingLine!.Amount = line.Amount.GetValueOrDefault();
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
        [Route("PostSalesInvoice")]
        public IActionResult PostSalesInvoice([FromBody] Dto.Sales.SalesInvoice salesInvoiceDto)
        {
            string[]? errors = null;

            _logger.LogInformation("PostSalesInvoice");

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
        [Route("SaveSalesInvoice")]
        public IActionResult SaveSalesInvoice([FromBody] Dto.Sales.SalesInvoice salesInvoiceDto)
        {
            string[]? errors = null;

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
                Core.Domain.Sales.SalesInvoiceHeader? salesInvoice = null;
                Core.Domain.Sales.SalesOrderHeader? salesOrder = null;

                // Creating a new invoice
                if (isNew)
                {
                    var defaultPaymentTerm = _adminService.GetPaymentTerms().FirstOrDefault();
                    int? paymentTermId = (salesInvoiceDto.PaymentTermId.HasValue && salesInvoiceDto.PaymentTermId.Value > 0)
                        ? salesInvoiceDto.PaymentTermId.Value
                        : defaultPaymentTerm?.Id;

                    if (!salesInvoiceDto.FromSalesOrderId.HasValue)
                    {
                        salesOrder = new Core.Domain.Sales.SalesOrderHeader();
                        salesOrder.Date = salesInvoiceDto.InvoiceDate;
                        salesOrder.PaymentTermId = paymentTermId;
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
                    salesInvoice.CustomerId = salesInvoiceDto.CustomerId;
                    salesInvoice.Date = salesInvoiceDto.InvoiceDate;
                    salesInvoice.PaymentTermId = paymentTermId;
                    salesInvoice.ReferenceNo = salesInvoiceDto.ReferenceNo;
                    salesInvoice.Status = salesInvoiceDto.Status == "Draft" 
                        ? Core.Domain.SalesInvoiceStatus.Draft 
                        : Core.Domain.SalesInvoiceStatus.Open;

                    // Resolve default service item and measurement if omitted for flexible invoicing
                    var items = _inventoryService.GetAllItems().ToList();
                    var defaultItem = items.FirstOrDefault(i => i.ItemCategory != null && i.ItemCategory.ItemType == ItemTypes.Service) 
                        ?? items.FirstOrDefault();
                    var defaultMeasurement = defaultItem?.SellMeasurementId 
                        ?? defaultItem?.SmallestMeasurementId 
                        ?? _inventoryService.GetMeasurements().FirstOrDefault()?.Id 
                        ?? 1;

                    foreach (var line in salesInvoiceDto.SalesInvoiceLines!)
                    {
                        var salesInvoiceLine = new Core.Domain.Sales.SalesInvoiceLine();

                        salesInvoiceLine.Amount = line.Amount.GetValueOrDefault();
                        salesInvoiceLine.Discount = line.Discount.GetValueOrDefault();
                        salesInvoiceLine.Quantity = line.Quantity.GetValueOrDefault(1);

                        int lineItemId = line.ItemId.GetValueOrDefault();
                        int lineMeasurementId = line.MeasurementId.GetValueOrDefault();

                        if (lineItemId == 0 && !string.IsNullOrWhiteSpace(line.ItemDescription))
                        {
                            var existing = items.FirstOrDefault(i => i.Description.Equals(line.ItemDescription, StringComparison.OrdinalIgnoreCase));
                            if (existing != null)
                            {
                                lineItemId = existing.Id;
                                lineMeasurementId = existing.SellMeasurementId ?? existing.SmallestMeasurementId ?? defaultMeasurement;
                            }
                        }

                        if (lineItemId == 0 && defaultItem != null)
                        {
                            lineItemId = defaultItem.Id;
                        }
                        if (lineMeasurementId == 0)
                        {
                            lineMeasurementId = defaultMeasurement;
                        }

                        salesInvoiceLine.ItemId = lineItemId;
                        salesInvoiceLine.MeasurementId = lineMeasurementId;
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
                            salesOrderLine.Amount = salesInvoiceLine.Amount;
                            salesOrderLine.Discount = salesInvoiceLine.Discount;
                            salesOrderLine.Quantity = salesInvoiceLine.Quantity;
                            salesOrderLine.ItemId = salesInvoiceLine.ItemId;
                            salesOrderLine.MeasurementId = salesInvoiceLine.MeasurementId;
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

                    var defaultPaymentTerm = _adminService.GetPaymentTerms().FirstOrDefault();
                    salesInvoice.Date = salesInvoiceDto.InvoiceDate;
                    salesInvoice.PaymentTermId = (salesInvoiceDto.PaymentTermId.HasValue && salesInvoiceDto.PaymentTermId.Value > 0)
                        ? salesInvoiceDto.PaymentTermId.Value
                        : (salesInvoice.PaymentTermId ?? defaultPaymentTerm?.Id);
                    salesInvoice.ReferenceNo = salesInvoiceDto.ReferenceNo;
                    salesInvoice.CustomerId = salesInvoiceDto.CustomerId;
                    if (!string.IsNullOrWhiteSpace(salesInvoiceDto.Status))
                    {
                        salesInvoice.Status = salesInvoiceDto.Status == "Draft" 
                            ? Core.Domain.SalesInvoiceStatus.Draft 
                            : Core.Domain.SalesInvoiceStatus.Open;
                    }

                    var items = _inventoryService.GetAllItems().ToList();
                    var defaultItem = items.FirstOrDefault(i => i.ItemCategory != null && i.ItemCategory.ItemType == ItemTypes.Service) 
                        ?? items.FirstOrDefault();
                    var defaultMeasurement = defaultItem?.SellMeasurementId 
                        ?? defaultItem?.SmallestMeasurementId 
                        ?? _inventoryService.GetMeasurements().FirstOrDefault()?.Id 
                        ?? 1;

                    foreach (var line in salesInvoiceDto.SalesInvoiceLines!)
                    {
                        var existingLine = salesInvoice.SalesInvoiceLines.Where(id => id.Id == line.Id).FirstOrDefault();

                        int lineItemId = line.ItemId.GetValueOrDefault();
                        int lineMeasurementId = line.MeasurementId.GetValueOrDefault();

                        if (lineItemId == 0 && !string.IsNullOrWhiteSpace(line.ItemDescription))
                        {
                            var existing = items.FirstOrDefault(i => i.Description.Equals(line.ItemDescription, StringComparison.OrdinalIgnoreCase));
                            if (existing != null)
                            {
                                lineItemId = existing.Id;
                                lineMeasurementId = existing.SellMeasurementId ?? existing.SmallestMeasurementId ?? defaultMeasurement;
                            }
                        }

                        if (lineItemId == 0 && defaultItem != null)
                        {
                            lineItemId = defaultItem.Id;
                        }
                        if (lineMeasurementId == 0)
                        {
                            lineMeasurementId = defaultMeasurement;
                        }

                        if (existingLine != null)
                        {
                            existingLine.Amount = line.Amount.GetValueOrDefault();
                            existingLine.Discount = line.Discount.GetValueOrDefault();
                            existingLine.Quantity = line.Quantity.GetValueOrDefault(1);
                            existingLine.ItemId = lineItemId;
                            existingLine.MeasurementId = lineMeasurementId;
                        }
                        else
                        {
                            //if you reach here, this line item is newly added to invoice. also, it has no SalesOrderLineId.
                            var salesInvoiceLine = new Core.Domain.Sales.SalesInvoiceLine();
                            salesInvoiceLine.Amount = line.Amount.GetValueOrDefault();
                            salesInvoiceLine.Discount = line.Discount.GetValueOrDefault();
                            salesInvoiceLine.Quantity = line.Quantity.GetValueOrDefault(1);
                            salesInvoiceLine.ItemId = lineItemId;
                            salesInvoiceLine.MeasurementId = lineMeasurementId;
                            salesInvoice.SalesInvoiceLines.Add(salesInvoiceLine);

                            // add a new order line.
                            var salesOrderLine = new Core.Domain.Sales.SalesOrderLine();
                            salesOrderLine.Amount = salesInvoiceLine.Amount;
                            salesOrderLine.Discount = salesInvoiceLine.Discount;
                            salesOrderLine.Quantity = salesInvoiceLine.Quantity;
                            salesOrderLine.ItemId = salesInvoiceLine.ItemId;
                            salesOrderLine.MeasurementId = salesInvoiceLine.MeasurementId;

                            // you will retrieve salesorder one time.
                            if (salesOrder == null)
                            {
                                salesOrder = _salesService.GetSalesOrderLineById(existingLine?.SalesOrderLine?.SalesOrderHeaderId ?? 0)?.SalesOrderHeader;
                                if (salesOrder != null)
                                {
                                    salesOrder.SalesOrderLines.Add(salesOrderLine);
                                }
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

                _logger.LogInformation("SaveSalesInvoice API " + salesInvoice.CustomerId);

                _salesService.SaveSalesInvoice(salesInvoice, salesOrder);

                // Auto-post to GL if issued (not draft) and not already posted
                if (salesInvoiceDto.Status != "Draft" && salesInvoice.Id > 0 && !salesInvoice.GeneralLedgerHeaderId.HasValue)
                {
                    try
                    {
                        _salesService.PostSalesInvoice(salesInvoice.Id);
                    }
                    catch (Exception postEx)
                    {
                        _logger.LogWarning(postEx, "Invoice {InvoiceId} saved but GL auto-posting failed: {Error}", salesInvoice.Id, postEx.Message);
                    }
                }

                return Ok(new { id = salesInvoice.Id, no = salesInvoice.No, customerId = salesInvoice.CustomerId });
            }
            catch (Exception ex)
            {
                errors = new string[1] { ex.InnerException != null ? ex.InnerException.Message : ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }

        [HttpPost("CreateSalesInvoice")]
        [ServiceFilter(typeof(ValidationFilterAttribute))]
        public IActionResult CreateSalesInvoice([FromBody] Dto.Sales.SalesInvoice salesInvoiceDto)
        {
            var result = _salesService.CreateSalesInvoice(salesInvoiceDto);

            if (result.IsFailure)
                return BadRequest(result.Error.Message);

            var salesInvoiceToReturn = result.Value;

            return Ok(salesInvoiceToReturn);
        }

        [HttpPost("UpdateSalesInvoice")]
        [ServiceFilter(typeof(ValidationFilterAttribute))]
        public IActionResult UpdateSalesInvoice([FromBody] Dto.Sales.SalesInvoice salesInvoiceDto)
        {
            var result = _salesService.UpdateSalesInvoice(salesInvoiceDto);

            if (result.IsFailure)
                return BadRequest(result.Error.Message);

            var salesInvoiceToReturn = result.Value;

            return Ok(salesInvoiceToReturn);
        }

        [HttpDelete("DeleteSalesInvoice")]
        public async Task<IActionResult> DeleteSalesInvoice(int id)
        {
            var result = await _salesService.DeleteSalesInvoiceAsync(id);

            if (result.IsFailure)
                return BadRequest(result.Error.Message);

            return NoContent();
        }

        [HttpPost]
        [Route("RecordPayment")]
        public IActionResult RecordPayment([FromBody] RecordInvoicePaymentDto paymentDto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var invoice = _salesService.GetSalesInvoiceById(paymentDto.InvoiceId);
                if (invoice == null)
                    return NotFound(new { message = $"Invoice #{paymentDto.InvoiceId} not found." });

                var cashBanks = _financialService.GetCashAndBanks();
                var primaryBank = cashBanks.FirstOrDefault();
                int accountToDebitId = primaryBank?.AccountId ?? 
                    _financialService.GetAccounts().FirstOrDefault(a => a.IsCash || a.AccountCode == "10110")?.Id ?? 
                    _financialService.GetAccounts().FirstOrDefault(a => a.AccountClassId == 1)?.Id ?? 1;

                int accountToCreditId = invoice.Customer?.AccountsReceivableAccountId ?? 
                    _financialService.GetAccountByAccountCode("10120")?.Id ?? 
                    _financialService.GetAccounts().FirstOrDefault(a => a.AccountClassId == 1)?.Id ?? 1;

                var paymentDate = paymentDto.PaymentDate == default ? DateTime.UtcNow : paymentDto.PaymentDate;

                // 1. Create and post SalesReceiptHeader (DR Cash, CR Accounts Receivable)
                var receipt = new Core.Domain.Sales.SalesReceiptHeader
                {
                    CustomerId = invoice.CustomerId,
                    Date = paymentDate,
                    Amount = paymentDto.Amount,
                    AccountToDebitId = accountToDebitId,
                    PaymentMethod = string.IsNullOrWhiteSpace(paymentDto.PaymentMethod) ? "Bank Transfer" : paymentDto.PaymentMethod,
                    ReferenceNo = string.IsNullOrWhiteSpace(paymentDto.ReferenceNo) ? invoice.No : paymentDto.ReferenceNo,
                    Memo = paymentDto.Notes
                };

                receipt.SalesReceiptLines.Add(new Core.Domain.Sales.SalesReceiptLine
                {
                    Quantity = 1,
                    Amount = paymentDto.Amount,
                    AmountPaid = paymentDto.Amount,
                    AccountToCreditId = accountToCreditId
                });

                _salesService.AddSalesReceipt(receipt);

                // 2. Add CustomerAllocation linking receipt to invoice
                var allocation = new Core.Domain.Sales.CustomerAllocation
                {
                    CustomerId = invoice.CustomerId,
                    Date = paymentDate,
                    SalesInvoiceHeaderId = invoice.Id,
                    SalesReceiptHeaderId = receipt.Id,
                    Amount = paymentDto.Amount
                };
                invoice.CustomerAllocations.Add(allocation);

                // Update invoice status if fully paid
                if (invoice.IsFullPaid())
                {
                    invoice.Status = Core.Domain.SalesInvoiceStatus.Closed;
                }
                else
                {
                    invoice.Status = Core.Domain.SalesInvoiceStatus.Open;
                }
                _salesService.UpdateSalesInvoice(invoice);

                return Ok(new
                {
                    success = true,
                    receiptId = receipt.Id,
                    receiptNo = receipt.No,
                    amountPaid = paymentDto.Amount,
                    invoiceId = invoice.Id,
                    invoiceNo = invoice.No,
                    isFullyPaid = invoice.IsFullPaid()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record payment for invoice {InvoiceId}", paymentDto.InvoiceId);
                return BadRequest(new { message = ex.InnerException != null ? ex.InnerException.Message : ex.Message });
            }
        }

        [HttpPost]
        [Route("VoidSalesInvoice")]
        public IActionResult VoidSalesInvoice(int id)
        {
            try
            {
                var invoice = _salesService.GetSalesInvoiceById(id);
                if (invoice == null)
                    return NotFound(new { message = $"Invoice #{id} not found." });

                decimal allocated = invoice.CustomerAllocations != null ? invoice.CustomerAllocations.Sum(a => a.Amount) : 0;
                if (allocated > 0)
                {
                    return BadRequest(new { message = "Cannot void an invoice with recorded payments. Please unallocate or refund payments first." });
                }

                invoice.Status = Core.Domain.SalesInvoiceStatus.Void;
                _salesService.UpdateSalesInvoice(invoice);

                return Ok(new { success = true, invoiceId = invoice.Id, status = "Void" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to void invoice {InvoiceId}", id);
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost]
        [Route("SaveQuotation")]
        public IActionResult SaveQuotation([FromBody] Dto.Sales.SalesQuotation quotationDto)
        {
            string[]? errors = null;
            _logger.LogInformation("SaveQuotation");

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
                Core.Domain.Sales.SalesQuoteHeader? salesQuote = null;

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
                            existingLine!.Amount = line.Amount == null ? 0 : line.Amount.Value;
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
        [Route("SaveReceipt")]
        public IActionResult SaveReceipt([FromBody] dynamic receiptDto)
        {
            string[]? errors = null;

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
                salesReceipt.AccountToDebitId = bank!.AccountId;
                salesReceipt.Amount = receiptDto.Amount;
                salesReceipt.PaymentMethod = receiptDto.PaymentMethod != null ? (string)receiptDto.PaymentMethod : null;
                salesReceipt.ReferenceNo = receiptDto.ReferenceNo != null ? (string)receiptDto.ReferenceNo : null;
                salesReceipt.Memo = receiptDto.Memo != null ? (string)receiptDto.Memo : null;

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
        [Route("UpdateReceipt")]
        public IActionResult UpdateReceipt([FromBody] dynamic receiptDto)
        {
            string[]? errors = null;

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

                int receiptId = receiptDto.Id;
                var existingReceipt = _salesService.GetSalesReceiptById(receiptId);

                if (existingReceipt == null)
                {
                    return new BadRequestObjectResult(new string[] { "Sales receipt not found." });
                }

                var bank = _financialService.GetCashAndBanks().Where(id => id.Id == (int)receiptDto.AccountToDebitId).FirstOrDefault();

                existingReceipt.Date = receiptDto.ReceiptDate;
                existingReceipt.CustomerId = receiptDto.CustomerId;
                existingReceipt.AccountToDebitId = bank!.AccountId;
                existingReceipt.Amount = receiptDto.Amount;
                existingReceipt.PaymentMethod = receiptDto.PaymentMethod != null ? (string)receiptDto.PaymentMethod : null;
                existingReceipt.ReferenceNo = receiptDto.ReferenceNo != null ? (string)receiptDto.ReferenceNo : null;
                existingReceipt.Memo = receiptDto.Memo != null ? (string)receiptDto.Memo : null;

                var customer = _salesService.GetCustomerById((int)receiptDto.CustomerId);
                if (customer.CustomerAdvancesAccountId != (int)receiptDto.AccountToCreditId)
                    throw new Exception("Invalid account.");

                // Update the first receipt line
                var receiptLine = existingReceipt.SalesReceiptLines.FirstOrDefault();
                if (receiptLine != null)
                {
                    receiptLine.AccountToCreditId = receiptDto.AccountToCreditId;
                    receiptLine.AmountPaid = receiptDto.Amount;
                    receiptLine.Amount = receiptDto.Amount;
                }

                _salesService.UpdateSalesReceipt(existingReceipt);

                return new ObjectResult(Ok());
            }
            catch (Exception ex)
            {
                errors = new string[1] { ex.InnerException != null ? ex.InnerException.Message : ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }

        [HttpPost]
        [Route("SaveAllocation")]
        public IActionResult SaveAllocation([FromBody] dynamic allocationDto)
        {
            string[]? errors = null;

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
        [Route("GetMonthlySales")] // api/Sales/GetMonthlySales
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
        [Route("SalesInvoiceForPrinting")]
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
        [Route("BookQuotation")]
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