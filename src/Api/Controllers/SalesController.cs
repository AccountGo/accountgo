using Api.ActionFilters;
using Api.Filters;
using Core.Domain;
using Dto.Sales;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Services.Administration;
using Services.Financial;
using Services.Inventory;
using Services.Sales;
using Services.TaxSystem;

namespace Api.Controllers
{
    [Core.Tenancy.TenantScoped]
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
        private readonly Core.Tenancy.ITenantContext _tenantContext;
        private readonly Core.Tenancy.ITenantGuard _tenantGuard;
        private readonly Api.Data.ApiDbContext _db;

        public SalesController(IAdministrationService adminService,
            ISalesService salesService,
            IFinancialService financialService,
            IInventoryService inventoryService,
            ITaxService taxService,
            ILogger<SalesController> logger,
            Core.Tenancy.ITenantContext tenantContext,
            Core.Tenancy.ITenantGuard tenantGuard,
            Api.Data.ApiDbContext db)
        {
            _adminService = adminService;
            _salesService = salesService;
            _financialService = financialService;
            _inventoryService = inventoryService;
            _taxService = taxService;
            _logger = logger;
            _tenantContext = tenantContext;
            _tenantGuard = tenantGuard;
            _db = db;
        }

        [HttpPost]
        [Route("SaveCustomer")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesCustomersManage)]
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
                customer = _tenantGuard.EnsureOwned<Core.Domain.Sales.Customer>(customerDto.Id, nameof(customerDto.Id));
            }

            if (customerDto.AccountsReceivableId.HasValue && customerDto.AccountsReceivableId.Value > 0)
                _tenantGuard.EnsureOwned<Core.Domain.Financials.Account>(customerDto.AccountsReceivableId.Value, nameof(customerDto.AccountsReceivableId));
            if (customerDto.SalesAccountId.HasValue && customerDto.SalesAccountId.Value > 0)
                _tenantGuard.EnsureOwned<Core.Domain.Financials.Account>(customerDto.SalesAccountId.Value, nameof(customerDto.SalesAccountId));
            if (customerDto.PrepaymentAccountId.HasValue && customerDto.PrepaymentAccountId.Value > 0)
                _tenantGuard.EnsureOwned<Core.Domain.Financials.Account>(customerDto.PrepaymentAccountId.Value, nameof(customerDto.PrepaymentAccountId));
            if (customerDto.SalesDiscountAccountId.HasValue && customerDto.SalesDiscountAccountId.Value > 0)
                _tenantGuard.EnsureOwned<Core.Domain.Financials.Account>(customerDto.SalesDiscountAccountId.Value, nameof(customerDto.SalesDiscountAccountId));
            if (customerDto.PaymentTermId.HasValue && customerDto.PaymentTermId.Value > 0)
                _tenantGuard.EnsureOwned<Core.Domain.PaymentTerm>(customerDto.PaymentTermId.Value, nameof(customerDto.PaymentTermId));
            if (customerDto.TaxGroupId.HasValue && customerDto.TaxGroupId.Value > 0)
                _tenantGuard.EnsureOwned<Core.Domain.TaxSystem.TaxGroup>(customerDto.TaxGroupId.Value, nameof(customerDto.TaxGroupId));

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
            if (customerDto.AccountsReceivableId.HasValue && customerDto.AccountsReceivableId.Value > 0)
                customer.AccountsReceivableAccountId = customerDto.AccountsReceivableId.Value;
            if (customerDto.SalesAccountId.HasValue && customerDto.SalesAccountId.Value > 0)
                customer.SalesAccountId = customerDto.SalesAccountId.Value;
            if (customerDto.PrepaymentAccountId.HasValue && customerDto.PrepaymentAccountId.Value > 0)
                customer.CustomerAdvancesAccountId = customerDto.PrepaymentAccountId.Value;
            if (customerDto.SalesDiscountAccountId.HasValue && customerDto.SalesDiscountAccountId.Value > 0)
                customer.SalesDiscountAccountId = customerDto.SalesDiscountAccountId.Value;
            ApplyDefaultAccounts(customer);

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

        /// <summary>Fills in the receivable, sales, advances and discount accounts a customer has no account for yet.</summary>
        private void ApplyDefaultAccounts(Core.Domain.Sales.Customer customer)
        {
            var accounts = _financialService.GetAccounts().ToList();
            customer.AccountsReceivableAccountId ??= (accounts.FirstOrDefault(a => a.AccountCode == "10120") ?? accounts.FirstOrDefault(a => a.AccountClassId == 1))?.Id;
            customer.SalesAccountId ??= (accounts.FirstOrDefault(a => a.AccountCode == "40100") ?? accounts.FirstOrDefault(a => a.AccountClassId == 4))?.Id;
            customer.CustomerAdvancesAccountId ??= (accounts.FirstOrDefault(a => a.AccountCode == "20120") ?? accounts.FirstOrDefault(a => a.AccountClassId == 2))?.Id;
            customer.SalesDiscountAccountId ??= accounts.FirstOrDefault(a => a.AccountCode == "40400")?.Id;
        }

        [HttpGet]
        [Route("Customer")]
        public IActionResult Customer(int id)
        {
            try
            {
                var customer = _salesService.GetCustomerById(id);
                if (customer == null)
                    return NotFound(new { message = $"Customer #{id} not found." });

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

        // Clients: the customers the Sales pages list and edit. Saving a client changes only the details below; the
        // invoices already issued to it keep the bill-to they were issued with.

        [HttpGet]
        [Route("Clients")] // api/Sales/Clients
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesCustomersView)]
        public IActionResult Clients()
        {
            var clients = _db.Customers.Include(c => c.Party)
                .Where(c => c.Party != null)
                .OrderBy(c => c.Party.Name)
                .AsEnumerable()
                .Select(c => ToClient(c))
                .ToList();
            AddBalances(clients, customerId: null);
            return Ok(clients);
        }

        [HttpGet]
        [Route("Client")] // api/Sales/Client?id=
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesCustomersView)]
        public IActionResult Client(int id)
        {
            var customer = _db.Customers.Include(c => c.Party).FirstOrDefault(c => c.Id == id && c.Party != null);
            if (customer == null)
                return NotFound(new { message = "Client not found." });

            var client = ToClient(customer);
            client.CanDelete = !ClientHasRecords(id);
            AddBalances(new[] { client }, customerId: id);
            return Ok(client);
        }

        /// <summary>
        /// What each client owes, from the same invoice totals and statuses the invoice pages show. Drafts and voided
        /// invoices are not owed.
        /// </summary>
        private void AddBalances(IEnumerable<Dto.Sales.Client> clients, int? customerId)
        {
            var byId = clients.ToDictionary(c => c.Id);
            var terms = _adminService.GetPaymentTerms().ToList();
            foreach (var invoice in _salesService.GetSalesInvoices().Where(i => customerId == null || i.CustomerId == customerId))
            {
                if (!byId.TryGetValue(invoice.CustomerId, out var client))
                    continue;
                var summary = Summarize(invoice, invoice.ResolveDueDate(terms.FirstOrDefault(t => t.Id == invoice.PaymentTermId)));
                if (summary.Status is "Draft" or "Void")
                    continue;

                client.Outstanding += summary.BalanceDue;
                if (summary.Status == "Overdue")
                    client.Overdue += summary.BalanceDue;
                client.PaidToDate += summary.Allocated;
                if (client.LastInvoiceDate == null || invoice.Date > client.LastInvoiceDate)
                    client.LastInvoiceDate = invoice.Date;
            }
        }

        [HttpPost]
        [Route("SaveClient")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesCustomersManage)]
        public IActionResult SaveClient([FromBody] Dto.Sales.Client clientDto)
        {
            var name = Blank(clientDto.Name);
            if (name == null)
                return BadRequest(new { message = "Enter the client's name." });
            if (name.Length > ClientNameMaxLength)
                return BadRequest(new { message = $"A client name can be at most {ClientNameMaxLength} characters." });
            var email = Blank(clientDto.Email);
            if (email != null && ValidEmail(email) == null)
                return BadRequest(new { message = "Enter a valid email address, or leave it blank." });
            int? paymentTermId = clientDto.PaymentTermId > 0 ? clientDto.PaymentTermId : null;
            if (paymentTermId.HasValue)
                _tenantGuard.EnsureOwned<Core.Domain.PaymentTerm>(paymentTermId.Value, nameof(clientDto.PaymentTermId));

            Core.Domain.Sales.Customer customer;
            bool isNew = clientDto.Id == 0;
            if (isNew)
            {
                var contactName = name.Split(' ', 2);
                customer = new Core.Domain.Sales.Customer
                {
                    Party = new Core.Domain.Party { PartyType = Core.Domain.PartyTypes.Customer, IsActive = true },
                    PrimaryContact = new Core.Domain.Contact
                    {
                        ContactType = Core.Domain.ContactTypes.Customer,
                        FirstName = contactName[0],
                        LastName = contactName.Length > 1 ? contactName[1] : "Contact",
                        Party = new Core.Domain.Party { PartyType = Core.Domain.PartyTypes.Contact, Name = name, Email = email }
                    }
                };
                ApplyDefaultAccounts(customer);
            }
            else
            {
                _tenantGuard.EnsureOwned<Core.Domain.Sales.Customer>(clientDto.Id, nameof(clientDto.Id));
                customer = _db.Customers.Include(c => c.Party).Single(c => c.Id == clientDto.Id);
            }

            customer.Party.Name = name;
            customer.Party.Email = email;
            customer.Party.Phone = Blank(clientDto.Phone);
            customer.Party.CompanyStreet = Blank(clientDto.Street);
            customer.Party.CompanyProvince = Blank(clientDto.City);
            customer.Party.CompanyPostalCode = Blank(clientDto.PostalCode);
            customer.Party.CompanyCountry = Blank(clientDto.Country);
            customer.PaymentTermId = paymentTermId;
            customer.ModifiedBy = GetUserNameFromRequestHeader();

            if (isNew)
                _salesService.AddCustomer(customer);
            else
                _salesService.UpdateCustomer(customer);

            var saved = ToClient(customer);
            saved.CanDelete = isNew || !ClientHasRecords(customer.Id);
            if (!isNew)
                AddBalances(new[] { saved }, customer.Id);
            return Ok(saved);
        }

        [HttpPost]
        [Route("ArchiveClient")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesCustomersManage)]
        public IActionResult ArchiveClient(int id, bool archived = true)
        {
            _tenantGuard.EnsureOwned<Core.Domain.Sales.Customer>(id, nameof(id));
            var customer = _db.Customers.Include(c => c.Party).Single(c => c.Id == id);
            customer.ArchivedAt = archived ? customer.ArchivedAt ?? DateTime.UtcNow : null;
            customer.ModifiedBy = GetUserNameFromRequestHeader();
            _salesService.UpdateCustomer(customer);

            var client = ToClient(customer);
            client.CanDelete = !ClientHasRecords(id);
            AddBalances(new[] { client }, id);
            return Ok(client);
        }

        /// <summary>
        /// Deletes a client nothing has been recorded for, such as one added by mistake. A client with invoices, orders,
        /// quotes or payments is archived instead, so its history stays intact.
        /// </summary>
        [HttpDelete]
        [Route("DeleteClient")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesCustomersManage)]
        public IActionResult DeleteClient(int id)
        {
            _tenantGuard.EnsureOwned<Core.Domain.Sales.Customer>(id, nameof(id));
            if (ClientHasRecords(id))
                return BadRequest(new { message = "This client has invoices or payments, so it can't be deleted. Archive it instead." });

            var customer = _db.Customers.Single(c => c.Id == id);
            var contact = customer.PrimaryContactId.HasValue ? _db.Contacts.FirstOrDefault(c => c.Id == customer.PrimaryContactId) : null;
            var parties = new[] { customer.PartyId, contact?.PartyId }
                .Where(partyId => partyId.HasValue)
                .Select(partyId => partyId!.Value)
                .Distinct()
                .ToList();

            // Saved in foreign-key order: D1 replays the statements in order and enforces the keys.
            using var tx = _db.Database.BeginTransaction();
            _db.RemoveRange(_db.Set<Core.Domain.CustomerContact>().Where(cc => cc.CustomerId == id));
            _db.Customers.Remove(customer);
            _db.SaveChanges();
            if (contact != null && !_db.Customers.Any(c => c.PrimaryContactId == contact.Id) && !_db.Vendors.Any(v => v.PrimaryContactId == contact.Id))
            {
                _db.RemoveRange(_db.Set<Core.Domain.CustomerContact>().Where(cc => cc.ContactId == contact.Id));
                _db.Contacts.Remove(contact);
                _db.SaveChanges();
            }
            // A party still used by another role (say, the same business as a vendor) stays.
            _db.Set<Core.Domain.Party>().RemoveRange(_db.Set<Core.Domain.Party>().Where(p => parties.Contains(p.Id)
                && !_db.Customers.Any(c => c.PartyId == p.Id)
                && !_db.Vendors.Any(v => v.PartyId == p.Id)
                && !_db.Contacts.Any(c => c.PartyId == p.Id)));
            _db.SaveChanges();
            tx.Commit();

            return NoContent();
        }

        private const int ClientNameMaxLength = 200;

        private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static Dto.Sales.Client ToClient(Core.Domain.Sales.Customer customer) => new()
        {
            Id = customer.Id,
            Name = customer.Party?.Name,
            Email = customer.Party?.Email,
            Phone = customer.Party?.Phone,
            Street = customer.Party?.CompanyStreet,
            City = customer.Party?.CompanyProvince,
            PostalCode = customer.Party?.CompanyPostalCode,
            Country = customer.Party?.CompanyCountry,
            PaymentTermId = customer.PaymentTermId,
            Archived = customer.ArchivedAt.HasValue,
        };

        /// <summary>Whether anything has been recorded for the client: invoices (drafts too), orders, quotes, deliveries or payments.</summary>
        private bool ClientHasRecords(int customerId) =>
            _db.SalesInvoiceHeaders.Any(i => i.CustomerId == customerId)
            || _db.SalesOrderHeaders.Any(o => o.CustomerId == customerId)
            || _db.SalesQuoteHeaders.Any(q => q.CustomerId == customerId)
            || _db.SalesDeliveryHeaders.Any(d => d.CustomerId == customerId)
            || _db.SalesReceiptHeaders.Any(r => r.CustomerId == customerId)
            || _db.CustomerAllocations.Any(a => a.CustomerId == customerId);

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
                    customerDto.PaymentTermId = customer.PaymentTermId;
                    customerDto.Archived = customer.ArchivedAt.HasValue;
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
                var salesInvoiceDto = BuildSalesInvoiceDto(id);
                if (salesInvoiceDto == null)
                    return NotFound(new { message = $"Invoice #{id} not found." });

                return new ObjectResult(salesInvoiceDto);
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex);
            }
        }

        [HttpGet]
        [Route("SalesInvoicePdf")]
        public IActionResult SalesInvoicePdf(int id)
        {
            var salesInvoiceDto = BuildSalesInvoiceDto(id);
            if (salesInvoiceDto == null)
                return NotFound(new { message = $"Invoice #{id} not found." });

            var document = new Api.Service.InvoicePdfDocument(
                salesInvoiceDto,
                _db.CompanySettings.Select(s => s.Currency).FirstOrDefault());

            var bytes = QuestPDF.Fluent.GenerateExtensions.GeneratePdf(document);
            return File(bytes, "application/pdf", $"Invoice-{document.InvoiceNumber}.pdf");
        }

        /// <summary>
        /// Emails an issued invoice to the customer with its PDF attached, and a blind copy to the person sending it.
        /// Separate from issuing: a failed send never un-issues the invoice, and the same call resends later.
        /// </summary>
        [HttpPost]
        [Route("SendSalesInvoiceEmail")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesCreate)]
        public async Task<IActionResult> SendSalesInvoiceEmail(int id,
            [FromServices] Api.Service.IEmailSender emailSender)
        {
            var invoice = BuildSalesInvoiceDto(id);
            if (invoice == null)
                return NotFound(new { message = $"Invoice #{id} not found." });

            if (invoice.Status is "Draft" or "Void")
                return BadRequest(new { message = $"Only issued invoices can be emailed; this one is {invoice.Status!.ToLowerInvariant()}." });

            // Sent to the customer's address on file now, which may have changed since the invoice was issued.
            var to = ValidEmail(invoice.RecipientEmail);
            if (to == null)
                return BadRequest(new { message = "This customer has no valid email address on record." });

            // Customer addresses are whatever the workspace typed in, so cap sends to protect our domain's sending reputation.
            // Counted from the audit trail, so the cap holds across restarts and instances.
            var hourAgo = DateTime.UtcNow.AddHours(-1);
            if (_db.SalesInvoiceEmails.Count(e => e.SentAt >= hourAgo) >= InvoiceEmailsPerHour)
                return StatusCode(StatusCodes.Status429TooManyRequests,
                    new { message = $"This workspace has reached its limit of {InvoiceEmailsPerHour} invoice emails an hour. Please try again later." });

            var senderEmail = ValidEmail(HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value);
            var replyTo = ValidEmail(invoice.Seller.Email) ?? senderEmail;
            var currency = _db.CompanySettings.Select(s => s.Currency).FirstOrDefault();

            var content = Api.Service.InvoiceEmailTemplate.Build(invoice, currency, canReply: replyTo != null);
            var pdf = QuestPDF.Fluent.GenerateExtensions.GeneratePdf(new Api.Service.InvoicePdfDocument(invoice, currency));
            var number = Api.Service.InvoiceFormat.Number(invoice);

            var message = new Api.Service.EmailMessage
            {
                To = to,
                Subject = content.Subject,
                Html = content.Html,
                Text = content.Text,
                FromName = $"{(string.IsNullOrWhiteSpace(invoice.Seller.Name) ? "AccountGo" : invoice.Seller.Name)} via AccountGo",
                ReplyTo = replyTo,
                Attachments = { new Api.Service.EmailAttachment { FileName = Api.Service.InvoiceEmailTemplate.PdfFileName(number), Content = pdf } },
            };
            if (senderEmail != null && !string.Equals(senderEmail, to, StringComparison.OrdinalIgnoreCase))
                message.Bcc.Add(senderEmail);

            string? providerMessageId;
            try
            {
                // Deliberately not tied to the request's abort token: behind the Workers container proxy a request can read as
                // aborted while it is still being served, and an email half-sent on cancel is worse than one that finishes.
                providerMessageId = await emailSender.SendAsync(message, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invoice {InvoiceId} email to the customer could not be sent", id);
                var reason = ex is Api.Service.EmailDeliveryException ? $" {ex.Message}" : string.Empty;
                return StatusCode(StatusCodes.Status502BadGateway, new { message = $"The email could not be sent.{reason}" });
            }

            var record = new Core.Domain.Sales.SalesInvoiceEmail
            {
                SalesInvoiceHeaderId = id,
                Kind = Core.Domain.Sales.SalesInvoiceEmail.InvoiceKind,
                ToEmail = to,
                BccEmail = message.Bcc.FirstOrDefault(),
                Subject = message.Subject.Length > 300 ? message.Subject[..300] : message.Subject,
                SentAt = DateTime.UtcNow,
                SentByUserId = HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                SentByEmail = senderEmail,
                ProviderMessageId = providerMessageId is { Length: > 100 } ? providerMessageId[..100] : providerMessageId,
            };
            try
            {
                _db.SalesInvoiceEmails.Add(record);
                _db.SaveChanges();
            }
            catch (Exception ex)
            {
                // The customer already has the email; a lost audit row must not make the user send it twice.
                _logger.LogError(ex, "Invoice {InvoiceId} was emailed to the customer but the send could not be recorded", id);
            }

            _logger.LogInformation("Invoice {InvoiceId} emailed by {Sender} for workspace {CompanyId}", id, senderEmail, _tenantContext.CompanyId);
            return Ok(new { success = true, to, bcc = message.Bcc, email = ToEmailItem(record) });
        }

        private const int InvoiceEmailsPerHour = 30;

        private static Dto.Sales.InvoiceEmailItemDto ToEmailItem(Core.Domain.Sales.SalesInvoiceEmail email) => new()
        {
            Id = email.Id,
            Kind = email.Kind,
            To = email.ToEmail,
            SentAt = DateTime.SpecifyKind(email.SentAt, DateTimeKind.Utc),
            SentBy = email.SentByEmail,
        };

        private static string? ValidEmail(string? value)
        {
            var trimmed = value?.Trim();
            return !string.IsNullOrEmpty(trimmed) && System.Net.Mail.MailAddress.TryCreate(trimmed, out var address) && address.Address == trimmed
                ? trimmed
                : null;
        }

        /// <summary>
        /// The due date sent with the invoice, or else the term's business days after the invoice date. A due date before
        /// the invoice date is refused.
        /// </summary>
        private DateTime DueDateForSave(DateTime invoiceDate, DateTime? requested, int? paymentTermId)
        {
            if (requested.HasValue)
            {
                if (requested.Value.Date < invoiceDate.Date)
                    throw new ArgumentException("The due date can't be before the invoice date.");
                return requested.Value.Date;
            }
            var term = paymentTermId.HasValue ? _adminService.GetPaymentTerms().FirstOrDefault(t => t.Id == paymentTermId) : null;
            return Core.Domain.BusinessDays.Add(invoiceDate, term?.DueAfterDays ?? 0);
        }

        private Core.Domain.Company? CurrentCompany() =>
            (!string.IsNullOrWhiteSpace(_tenantContext.CompanySlug) ? _adminService.GetCompanyBySlug(_tenantContext.CompanySlug) : null)
            ?? _adminService.GetCompanies().FirstOrDefault(c => c.Id == _tenantContext.CompanyId);

        /// <summary>Records the seller and the customer as they print, once the invoice is issued.</summary>
        private void CaptureIfIssued(Core.Domain.Sales.SalesInvoiceHeader invoice)
        {
            invoice.CaptureSellerIfIssued(CurrentCompany(), _db.CompanySettings.FirstOrDefault());
            if (invoice.BillToJson == null)
                invoice.CaptureBillToIfIssued(_db.Customers.Where(c => c.Id == invoice.CustomerId).Select(c => c.Party).FirstOrDefault());
        }

        /// <summary>An invoice's totals, balance and status, the same in every view of it.</summary>
        private sealed record InvoiceSummary(decimal TotalTax, decimal GrandTotal, decimal Allocated, decimal BalanceDue,
            string Status, Core.Domain.SalesInvoiceStatus StatusId);

        private InvoiceSummary Summarize(Core.Domain.Sales.SalesInvoiceHeader invoice, DateTime dueDate)
        {
            var tax = invoice.ComputeTotalTax();
            var grandTotal = invoice.GrandTotal();
            var allocated = Math.Round(invoice.CustomerAllocations?.Sum(a => a.Amount) ?? 0, 2, MidpointRounding.AwayFromZero);
            var balanceDue = Math.Round(Math.Max(0, grandTotal - allocated), 2, MidpointRounding.AwayFromZero);

            // Status follows the lifecycle: void, paid, partly paid, draft, overdue, then open.
            var (status, statusId) =
                invoice.Status == Core.Domain.SalesInvoiceStatus.Void ? ("Void", Core.Domain.SalesInvoiceStatus.Void)
                : invoice.Status == Core.Domain.SalesInvoiceStatus.Closed || (balanceDue <= 0.005m && allocated > 0) ? ("Paid", Core.Domain.SalesInvoiceStatus.Closed)
                : allocated > 0 && balanceDue > 0.005m ? ("Partially Paid", Core.Domain.SalesInvoiceStatus.Open)
                : invoice.Status == Core.Domain.SalesInvoiceStatus.Draft && allocated == 0 ? ("Draft", Core.Domain.SalesInvoiceStatus.Draft)
                : dueDate.Date < DateTime.UtcNow.Date && balanceDue > 0.005m ? ("Overdue", Core.Domain.SalesInvoiceStatus.Overdue)
                : ("Open", Core.Domain.SalesInvoiceStatus.Open);

            // A voided invoice is no longer owed, so nothing is due on it.
            if (statusId == Core.Domain.SalesInvoiceStatus.Void)
                balanceDue = 0;

            return new InvoiceSummary(tax, grandTotal, allocated, balanceDue, status, statusId);
        }

        private static void ApplySummary(Dto.Sales.SalesInvoice dto, InvoiceSummary summary)
        {
            dto.TotalTax = summary.TotalTax;
            dto.TotalAmountAfterTax = summary.GrandTotal;
            dto.TotalAllocatedAmount = summary.Allocated;
            dto.PaidAmount = summary.Allocated;
            dto.BalanceDue = summary.BalanceDue;
            dto.Status = summary.Status;
            dto.StatusId = (int)summary.StatusId;
        }

        /// <summary>Shapes an invoice with its computed totals and status; null when it is not in this workspace.</summary>
        private Dto.Sales.SalesInvoice? BuildSalesInvoiceDto(int id)
        {
            var salesInvoice = _salesService.GetSalesInvoiceById(id);
            if (salesInvoice == null)
                return null;

            var paymentTerm = _adminService.GetPaymentTerms().FirstOrDefault(t => t.Id == salesInvoice.PaymentTermId);
            var dueDate = salesInvoice.ResolveDueDate(paymentTerm);

            var defaultCompany = CurrentCompany();
            var setting = _db.CompanySettings.FirstOrDefault();
            var invoiceSettings = setting?.GetSalesInvoiceSettings() ?? new Core.Domain.SalesInvoiceSettings();
            // Issued invoices print the seller recorded when they were issued; drafts, and invoices issued before that
            // was recorded, follow the current business details.
            var seller = Core.Domain.Sales.InvoiceSeller.FromJson(salesInvoice.SellerJson)
                ?? Core.Domain.Sales.InvoiceSeller.FromWorkspace(defaultCompany, setting);

            // Likewise the customer: as recorded at issue, or as it is now for drafts.
            var party = salesInvoice.Customer?.Party;
            var billTo = Core.Domain.Sales.InvoiceBillTo.FromJson(salesInvoice.BillToJson)
                ?? Core.Domain.Sales.InvoiceBillTo.FromParty(party);

            var salesInvoiceDto = new Dto.Sales.SalesInvoice()
            {
                Id = salesInvoice.Id,
                CustomerId = salesInvoice.CustomerId,
                CustomerName = billTo.Name,
                CustomerEmail = billTo.Email,
                CustomerPhone = billTo.Phone,
                CustomerAddress = billTo.AddressText,
                RecipientEmail = party?.Email,
                No = salesInvoice.No,
                InvoiceDate = salesInvoice.Date,
                DueDate = dueDate,
                SalesInvoiceLines = new List<Dto.Sales.SalesInvoiceLine>(),
                PaymentTermId = salesInvoice.PaymentTermId,
                ReferenceNo = salesInvoice.ReferenceNo,
                ShippingHandlingCharge = salesInvoice.ShippingHandlingCharge,
                Notes = salesInvoice.Notes,
                PaymentInstructions = salesInvoice.PaymentInstructions,
                Posted = salesInvoice.GeneralLedgerHeaderId != null,
                CompanyName = defaultCompany?.Name,
                Seller = new Dto.Sales.SellerInfo
                {
                    Name = seller.Name,
                    TaxId = seller.TaxId,
                    AddressLines = invoiceSettings.ShowSellerAddress ? seller.AddressLines : new List<string>(),
                    Email = seller.Email,
                    Phone = seller.Phone
                },
                Display = new Dto.Sales.DisplayFlags
                {
                    ShowCustomerEmail = invoiceSettings.ShowCustomerEmail,
                    ShowNotes = invoiceSettings.ShowNotes,
                    ShowPaymentInstructions = invoiceSettings.ShowPaymentInstructions
                }
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

            salesInvoiceDto.Emails = _db.SalesInvoiceEmails
                .Where(e => e.SalesInvoiceHeaderId == salesInvoice.Id)
                .OrderByDescending(e => e.SentAt)
                .AsEnumerable()
                .Select(ToEmailItem)
                .ToList();

            foreach (var line in salesInvoice.SalesInvoiceLines)
            {
                salesInvoiceDto.SalesInvoiceLines.Add(new Dto.Sales.SalesInvoiceLine
                {
                    Id = line.Id,
                    Amount = line.Amount,
                    Discount = line.Discount,
                    Quantity = line.Quantity,
                    ItemId = line.ItemId,
                    MeasurementId = line.MeasurementId,
                    ItemDescription = line.DisplayDescription,
                    MeasurementDescription = line.Measurement?.Description ?? "EA",
                    LineTotal = line.Subtotal,
                    TaxId = line.TaxId,
                    TaxAmount = line.TaxAmount
                });
            }
            ApplySummary(salesInvoiceDto, Summarize(salesInvoice, dueDate));

            // is this journal entry ready for posting?
            if (!salesInvoiceDto.Posted && salesInvoiceDto.SalesInvoiceLines.Count >= 1)
            {
                salesInvoiceDto.ReadyForPosting = true;
            }

            return salesInvoiceDto;
        }

        [HttpPost]
        [Route("addsalesorder")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesCreate)]
        public IActionResult AddSalesOrder([FromBody] Dto.Sales.SalesOrder salesorderDto)
        {
            try
            {
                _tenantGuard.EnsureOwned<Core.Domain.Sales.Customer>(salesorderDto.CustomerId, nameof(salesorderDto.CustomerId));
                if (salesorderDto.PaymentTermId > 0)
                    _tenantGuard.EnsureOwned<Core.Domain.PaymentTerm>(salesorderDto.PaymentTermId, nameof(salesorderDto.PaymentTermId));

                var salesOrder = new Core.Domain.Sales.SalesOrderHeader()
                {
                    CustomerId = salesorderDto.CustomerId,
                    Date = salesorderDto.OrderDate,
                    PaymentTermId = salesorderDto.PaymentTermId,
                    ReferenceNo = salesorderDto.ReferenceNo
                };

                foreach (var line in salesorderDto.SalesOrderLines)
                {
                    if (line.ItemId.HasValue && line.ItemId.Value > 0)
                        _tenantGuard.EnsureOwned<Core.Domain.Items.Item>(line.ItemId.Value, nameof(line.ItemId));
                    if (line.MeasurementId.HasValue && line.MeasurementId.Value > 0)
                        _tenantGuard.EnsureOwned<Core.Domain.Items.Measurement>(line.MeasurementId.Value, nameof(line.MeasurementId));

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
            if (quote == null)
                return NotFound(new { message = $"Quotation #{id} not found." });

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
        [Route("SalesInvoices")] // api/Sales/SalesInvoices, or ?customerId= for one client's
        public IActionResult SalesInvoices(int? customerId = null)
        {
            var salesInvoices = _salesService.GetSalesInvoices()
                .Where(i => customerId == null || i.CustomerId == customerId)
                .OrderByDescending(i => i.Date).ThenByDescending(i => i.Id);
            IList<Dto.Sales.SalesInvoice> salesInvoicesDto = new List<Dto.Sales.SalesInvoice>();
            var paymentTerms = _adminService.GetPaymentTerms();

            foreach (var salesInvoice in salesInvoices)
            {
                var term = paymentTerms.FirstOrDefault(t => t.Id == salesInvoice.PaymentTermId);
                var dueDate = salesInvoice.ResolveDueDate(term);

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
                    ShippingHandlingCharge = salesInvoice.ShippingHandlingCharge,
                    Posted = salesInvoice.GeneralLedgerHeaderId != null
                };

                foreach (var line in salesInvoice.SalesInvoiceLines)
                {
                    salesInvoiceDto.SalesInvoiceLines!.Add(new Dto.Sales.SalesInvoiceLine()
                    {
                        Id = line.Id,
                        ItemId = line.ItemId,
                        MeasurementId = line.MeasurementId,
                        Quantity = line.Quantity,
                        Amount = line.Amount,
                        Discount = line.Discount,
                        ItemDescription = line.DisplayDescription,
                        MeasurementDescription = line.Measurement?.Description ?? "EA",
                        LineTotal = line.Subtotal,
                        TaxId = line.TaxId,
                        TaxAmount = line.TaxAmount
                    });
                }
                ApplySummary(salesInvoiceDto, Summarize(salesInvoice, dueDate));

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
            if (salesReceipt == null)
                return NotFound(new { message = $"Sales receipt #{id} not found." });

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
                        ShippingHandlingCharge = invoice.ShippingHandlingCharge,
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
                        lineDto.LineTotal = line.Subtotal;
                        lineDto.TaxId = line.TaxId;
                        lineDto.TaxAmount = line.TaxAmount;

                        invoiceDto.SalesInvoiceLines!.Add(lineDto);
                    }

                    ApplySummary(invoiceDto, Summarize(invoice, invoice.ResolveDueDate(
                        _adminService.GetPaymentTerms().FirstOrDefault(t => t.Id == invoice.PaymentTermId))));
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
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesCreate)]
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

                _tenantGuard.EnsureOwned<Core.Domain.Sales.Customer>(salesOrderDto.CustomerId, nameof(salesOrderDto.CustomerId));
                if (salesOrderDto.PaymentTermId > 0)
                    _tenantGuard.EnsureOwned<Core.Domain.PaymentTerm>(salesOrderDto.PaymentTermId, nameof(salesOrderDto.PaymentTermId));

                if (isNew)
                {
                    salesOrder = new Core.Domain.Sales.SalesOrderHeader();
                    salesOrder.Status = SalesOrderStatus.Open;

                    if (salesOrderDto.QuotationId != null)
                    {
                        var quotation = _tenantGuard.EnsureOwned<Core.Domain.Sales.SalesQuoteHeader>(salesOrderDto.QuotationId.Value, nameof(salesOrderDto.QuotationId));
                        quotation.Status = SalesQuoteStatus.ClosedOrderCreated;
                        _salesService.UpdateSalesQuote(quotation);
                    }

                }
                else
                {
                    salesOrder = _tenantGuard.EnsureOwned<Core.Domain.Sales.SalesOrderHeader>(salesOrderDto.Id, nameof(salesOrderDto.Id));
                }

                salesOrder.CustomerId = salesOrderDto.CustomerId;
                salesOrder.Date = salesOrderDto.OrderDate;
                salesOrder.PaymentTermId = salesOrderDto.PaymentTermId;
                salesOrder.ReferenceNo = salesOrderDto.ReferenceNo;

                foreach (var line in salesOrderDto.SalesOrderLines)
                {
                    if (line.ItemId.HasValue && line.ItemId.Value > 0)
                        _tenantGuard.EnsureOwned<Core.Domain.Items.Item>(line.ItemId.Value, nameof(line.ItemId));
                    if (line.MeasurementId.HasValue && line.MeasurementId.Value > 0)
                        _tenantGuard.EnsureOwned<Core.Domain.Items.Measurement>(line.MeasurementId.Value, nameof(line.MeasurementId));

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
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesPost)]
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

                using (var tx = BeginInvoiceNumberingTransaction())
                {
                    _salesService.PostSalesInvoice(salesInvoiceDto.Id);
                    tx.Commit();
                }

                return new ObjectResult(Ok());
            }
            catch (Exception ex)
            {
                if (ex is Core.Tenancy.TenantException) throw;
                if (InvoiceNumberProblem(ex) is { } numberProblem)
                    return StatusCode(numberProblem.Status, new[] { numberProblem.Message });
                errors = new string[1] { ex.InnerException != null ? ex.InnerException.Message : ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }

        [HttpPost]
        [Route("SaveSalesInvoice")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesCreate)]
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
                // Saving as anything but a draft issues the invoice, which takes the same permission as issuing it.
                bool issuing = isNew ? salesInvoiceDto.Status != "Draft" : !string.IsNullOrWhiteSpace(salesInvoiceDto.Status) && salesInvoiceDto.Status != "Draft";
                if (issuing && !_tenantContext.Permissions.Contains(Core.Domain.Security.SystemPermissions.SalesInvoicesPost))
                    return RequirePermissionAttribute.Denied(Core.Domain.Security.SystemPermissions.SalesInvoicesPost);

                Core.Domain.Sales.SalesInvoiceHeader? salesInvoice = null;
                Core.Domain.Sales.SalesOrderHeader? salesOrder = null;

                using var tx = BeginInvoiceNumberingTransaction();

                _tenantGuard.EnsureOwned<Core.Domain.Sales.Customer>(salesInvoiceDto.CustomerId, nameof(salesInvoiceDto.CustomerId));
                if (salesInvoiceDto.PaymentTermId.HasValue && salesInvoiceDto.PaymentTermId.Value > 0)
                    _tenantGuard.EnsureOwned<Core.Domain.PaymentTerm>(salesInvoiceDto.PaymentTermId.Value, nameof(salesInvoiceDto.PaymentTermId));
                foreach (var line in salesInvoiceDto.SalesInvoiceLines!)
                    _tenantGuard.EnsureOwned<Core.Domain.TaxSystem.Tax>(line.TaxId, nameof(line.TaxId));

                // Creating a new invoice
                if (isNew)
                {
                    var defaultPaymentTerm = _adminService.GetPaymentTerms().FirstOrDefault();
                    // No term with a due date means custom terms; with neither, the workspace default applies.
                    int? paymentTermId = (salesInvoiceDto.PaymentTermId.HasValue && salesInvoiceDto.PaymentTermId.Value > 0)
                        ? salesInvoiceDto.PaymentTermId.Value
                        : salesInvoiceDto.DueDate.HasValue ? null : defaultPaymentTerm?.Id;

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
                        salesOrder = _tenantGuard.EnsureOwned<Core.Domain.Sales.SalesOrderHeader>(salesInvoiceDto.FromSalesOrderId.GetValueOrDefault(), nameof(salesInvoiceDto.FromSalesOrderId));
                    }

                    // populate invoice header
                    salesInvoice = new Core.Domain.Sales.SalesInvoiceHeader();
                    salesInvoice.No = Core.Domain.Sales.InvoiceNumber.ForSave(null, null, exists: false, salesInvoiceDto.No);
                    salesInvoice.CustomerId = salesInvoiceDto.CustomerId;
                    salesInvoice.Date = salesInvoiceDto.InvoiceDate;
                    salesInvoice.PaymentTermId = paymentTermId;
                    salesInvoice.DueDate = DueDateForSave(salesInvoice.Date, salesInvoiceDto.DueDate, paymentTermId);
                    salesInvoice.ReferenceNo = salesInvoiceDto.ReferenceNo;
                    salesInvoice.Notes = string.IsNullOrWhiteSpace(salesInvoiceDto.Notes) ? null : salesInvoiceDto.Notes.Trim();
                    salesInvoice.PaymentInstructions = string.IsNullOrWhiteSpace(salesInvoiceDto.PaymentInstructions) ? null : salesInvoiceDto.PaymentInstructions.Trim();
                    salesInvoice.Status = salesInvoiceDto.Status == "Draft" 
                        ? Core.Domain.SalesInvoiceStatus.Draft 
                        : Core.Domain.SalesInvoiceStatus.Open;

                    // Resolve default service item and measurement if omitted for flexible invoicing
                    var items = _inventoryService.GetAllItems().ToList();
                    var defaultMeasurement = new Lazy<int>(DefaultMeasurementId);

                    foreach (var line in salesInvoiceDto.SalesInvoiceLines!)
                    {
                        if (line.ItemId.HasValue && line.ItemId.Value > 0)
                            _tenantGuard.EnsureOwned<Core.Domain.Items.Item>(line.ItemId.Value, nameof(line.ItemId));
                        if (line.MeasurementId.HasValue && line.MeasurementId.Value > 0)
                            _tenantGuard.EnsureOwned<Core.Domain.Items.Measurement>(line.MeasurementId.Value, nameof(line.MeasurementId));

                        var salesInvoiceLine = new Core.Domain.Sales.SalesInvoiceLine();

                        salesInvoiceLine.Amount = line.Amount.GetValueOrDefault();
                        salesInvoiceLine.Discount = line.Discount.GetValueOrDefault();
                        salesInvoiceLine.Quantity = line.Quantity.GetValueOrDefault(1);
                        salesInvoiceLine.TaxId = line.TaxId;

                        int lineItemId = line.ItemId.GetValueOrDefault();
                        int lineMeasurementId = line.MeasurementId.GetValueOrDefault();

                        if (lineItemId == 0)
                        {
                            var lineItem = ResolveFreeTextItem(items, line.ItemDescription);
                            lineItemId = lineItem.Id;
                            lineMeasurementId = lineItem.SellMeasurementId ?? lineItem.SmallestMeasurementId ?? defaultMeasurement.Value;
                        }
                        if (lineMeasurementId == 0)
                        {
                            lineMeasurementId = defaultMeasurement.Value;
                        }

                        salesInvoiceLine.ItemId = lineItemId;
                        salesInvoiceLine.MeasurementId = lineMeasurementId;
                        salesInvoiceLine.Description = Core.Domain.Sales.SalesInvoiceLine.DescriptionFor(line.ItemDescription, items.FirstOrDefault(i => i.Id == lineItemId));
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
                    // EnsureOwned does not load the lines; reload with them so existing lines are matched, not duplicated.
                    _tenantGuard.EnsureOwned<Core.Domain.Sales.SalesInvoiceHeader>(salesInvoiceDto.Id, nameof(salesInvoiceDto.Id));
                    salesInvoice = _salesService.GetSalesInvoiceById(salesInvoiceDto.Id);

                    if (salesInvoice.GeneralLedgerHeaderId.HasValue)
                        throw new Exception("Invoice is already posted. Update is not allowed.");

                    // Before the status changes: whether the number may still change depends on the stored status.
                    salesInvoice.No = Core.Domain.Sales.InvoiceNumber.ForSave(salesInvoice.No, salesInvoice.Status, exists: true, salesInvoiceDto.No);

                    var defaultPaymentTerm = _adminService.GetPaymentTerms().FirstOrDefault();
                    salesInvoice.Date = salesInvoiceDto.InvoiceDate;
                    salesInvoice.PaymentTermId = (salesInvoiceDto.PaymentTermId.HasValue && salesInvoiceDto.PaymentTermId.Value > 0)
                        ? salesInvoiceDto.PaymentTermId.Value
                        : salesInvoiceDto.DueDate.HasValue ? null : (salesInvoice.PaymentTermId ?? defaultPaymentTerm?.Id);
                    salesInvoice.DueDate = DueDateForSave(salesInvoice.Date, salesInvoiceDto.DueDate, salesInvoice.PaymentTermId);
                    salesInvoice.ReferenceNo = salesInvoiceDto.ReferenceNo;
                    salesInvoice.Notes = string.IsNullOrWhiteSpace(salesInvoiceDto.Notes) ? null : salesInvoiceDto.Notes.Trim();
                    salesInvoice.PaymentInstructions = string.IsNullOrWhiteSpace(salesInvoiceDto.PaymentInstructions) ? null : salesInvoiceDto.PaymentInstructions.Trim();
                    // A different customer means a different "Bill to"; it is recorded again below.
                    if (salesInvoice.CustomerId != salesInvoiceDto.CustomerId)
                        salesInvoice.BillToJson = null;
                    salesInvoice.CustomerId = salesInvoiceDto.CustomerId;
                    if (!string.IsNullOrWhiteSpace(salesInvoiceDto.Status))
                    {
                        salesInvoice.Status = salesInvoiceDto.Status == "Draft" 
                            ? Core.Domain.SalesInvoiceStatus.Draft 
                            : Core.Domain.SalesInvoiceStatus.Open;
                    }

                    var items = _inventoryService.GetAllItems().ToList();
                    var defaultMeasurement = new Lazy<int>(DefaultMeasurementId);

                    foreach (var line in salesInvoiceDto.SalesInvoiceLines!)
                    {
                        if (line.ItemId.HasValue && line.ItemId.Value > 0)
                            _tenantGuard.EnsureOwned<Core.Domain.Items.Item>(line.ItemId.Value, nameof(line.ItemId));
                        if (line.MeasurementId.HasValue && line.MeasurementId.Value > 0)
                            _tenantGuard.EnsureOwned<Core.Domain.Items.Measurement>(line.MeasurementId.Value, nameof(line.MeasurementId));

                        var existingLine = salesInvoice.SalesInvoiceLines.Where(id => id.Id == line.Id).FirstOrDefault();

                        int lineItemId = line.ItemId.GetValueOrDefault();
                        int lineMeasurementId = line.MeasurementId.GetValueOrDefault();

                        if (lineItemId == 0)
                        {
                            var lineItem = ResolveFreeTextItem(items, line.ItemDescription);
                            lineItemId = lineItem.Id;
                            lineMeasurementId = lineItem.SellMeasurementId ?? lineItem.SmallestMeasurementId ?? defaultMeasurement.Value;
                        }
                        if (lineMeasurementId == 0)
                        {
                            lineMeasurementId = defaultMeasurement.Value;
                        }

                        if (existingLine != null)
                        {
                            existingLine.Amount = line.Amount.GetValueOrDefault();
                            existingLine.Discount = line.Discount.GetValueOrDefault();
                            existingLine.Quantity = line.Quantity.GetValueOrDefault(1);
                            existingLine.TaxId = line.TaxId;
                            existingLine.ItemId = lineItemId;
                            existingLine.MeasurementId = lineMeasurementId;
                            existingLine.Description = Core.Domain.Sales.SalesInvoiceLine.DescriptionFor(line.ItemDescription, items.FirstOrDefault(i => i.Id == lineItemId));
                        }
                        else
                        {
                            //if you reach here, this line item is newly added to invoice. also, it has no SalesOrderLineId.
                            var salesInvoiceLine = new Core.Domain.Sales.SalesInvoiceLine();
                            salesInvoiceLine.Amount = line.Amount.GetValueOrDefault();
                            salesInvoiceLine.Discount = line.Discount.GetValueOrDefault();
                            salesInvoiceLine.Quantity = line.Quantity.GetValueOrDefault(1);
                            salesInvoiceLine.TaxId = line.TaxId;
                            salesInvoiceLine.ItemId = lineItemId;
                            salesInvoiceLine.MeasurementId = lineMeasurementId;
                            salesInvoiceLine.Description = Core.Domain.Sales.SalesInvoiceLine.DescriptionFor(line.ItemDescription, items.FirstOrDefault(i => i.Id == lineItemId));
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

                salesInvoice.ShippingHandlingCharge = Math.Round(salesInvoiceDto.ShippingHandlingCharge, 2, MidpointRounding.AwayFromZero);
                var stockWarnings = _salesService.GetSalesInvoiceStockWarnings(salesInvoice);
                CaptureIfIssued(salesInvoice);
                _salesService.SaveSalesInvoice(salesInvoice, salesOrder);
                tx.Commit();

                // Auto-post to GL if issued (not draft) and not already posted
                if (salesInvoiceDto.Status != "Draft" && salesInvoice.Id > 0 && !salesInvoice.GeneralLedgerHeaderId.HasValue)
                {
                    var postingError = TryPostSalesInvoice(salesInvoice.Id);
                    if (postingError != null)
                        stockWarnings.Add(postingError);
                }

                var saved = BuildSalesInvoiceDto(salesInvoice.Id)!;
                saved.Warnings = stockWarnings;
                return Ok(saved);
            }
            catch (Exception ex)
            {
                if (ex is Core.Tenancy.TenantException) throw;
                if (InvoiceNumberProblem(ex) is { } numberProblem)
                    return StatusCode(numberProblem.Status, new[] { numberProblem.Message });
                errors = new string[1] { ex.InnerException != null ? ex.InnerException.Message : ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }

        [HttpGet]
        [Route("NextSalesInvoiceNumber")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesCreate)]
        public IActionResult NextSalesInvoiceNumber()
        {
            // A preview only: the number is taken when an invoice is issued, and may have moved on by then.
            return Ok(new { next = _salesService.PeekNextInvoiceNumber() });
        }

        // On SQLite, Serializable is BEGIN IMMEDIATE: the write lock is taken up front, so concurrent saves that
        // number invoices queue behind each other instead of both reading the same next number.
        private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction BeginInvoiceNumberingTransaction() =>
            Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.BeginTransaction(_db.Database, System.Data.IsolationLevel.Serializable);

        // These callers deliberately keep an issued invoice even when posting fails. Roll back and discard tracked
        // changes before a later receipt or allocation can accidentally persist part of that failed attempt.
        private string? TryPostSalesInvoice(int invoiceId)
        {
            var sync = HttpContext.RequestServices.GetRequiredService<Api.Service.D1Sync.ID1SyncContext>();
            var savedStatements = sync.GetStatements().ToList();
            try
            {
                using (var tx = BeginInvoiceNumberingTransaction())
                {
                    _salesService.PostSalesInvoice(invoiceId);
                    tx.Commit();
                }
                return null;
            }
            catch (Exception ex)
            {
                _db.ChangeTracker.Clear();
                sync.Clear();
                foreach (var statement in savedStatements)
                    sync.AddStatement(statement);
                _logger.LogWarning(ex, "Invoice {InvoiceId} remains unposted: {Error}", invoiceId, ex.Message);
                return ex.Message;
            }
        }

        private sealed record NumberProblem(int Status, string Message, string Code);

        // Our own number checks, or the unique index on (CompanyId, No) if a save got past them.
        private static NumberProblem? InvoiceNumberProblem(Exception ex)
        {
            if (ex is Core.Domain.Sales.InvoiceNumberException numberEx)
            {
                return numberEx.Error switch
                {
                    Core.Domain.Sales.InvoiceNumberError.Taken => new NumberProblem(StatusCodes.Status409Conflict, numberEx.Message, "number_taken"),
                    Core.Domain.Sales.InvoiceNumberError.Changed => new NumberProblem(StatusCodes.Status409Conflict, numberEx.Message, "number_changed"),
                    _ => new NumberProblem(StatusCodes.Status400BadRequest, numberEx.Message, "number_invalid"),
                };
            }

            var detail = ex.InnerException?.Message ?? string.Empty;
            if (ex is Microsoft.EntityFrameworkCore.DbUpdateException
                && (detail.Contains("SalesInvoiceHeader.No", StringComparison.Ordinal)
                    || detail.Contains("IX_SalesInvoiceHeader_CompanyId_No", StringComparison.Ordinal)))
            {
                return new NumberProblem(StatusCodes.Status409Conflict,
                    "That invoice number was just taken by another invoice. Please try again.", "number_taken");
            }
            return null;
        }

        [HttpPost("CreateSalesInvoice")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesCreate)]
        [ServiceFilter(typeof(ValidationFilterAttribute))]
        public IActionResult CreateSalesInvoice([FromBody] Dto.Sales.SalesInvoice salesInvoiceDto)
        {
            EnsureSalesInvoiceReferencesOwned(salesInvoiceDto);
            Core.Domain.Error.Result<Dto.Sales.SalesInvoice> result;
            try
            {
                using var tx = BeginInvoiceNumberingTransaction();
                result = _salesService.CreateSalesInvoice(salesInvoiceDto);
                if (result.IsSuccess) tx.Commit();
            }
            catch (Exception ex) when (InvoiceNumberProblem(ex) is { } numberProblem)
            {
                return StatusCode(numberProblem.Status, numberProblem.Message);
            }

            if (result.IsFailure)
                return BadRequest(result.Error.Message);

            return Ok(BuildSalesInvoiceDto(result.Value.Id));
        }

        [HttpPost("UpdateSalesInvoice")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesCreate)]
        [ServiceFilter(typeof(ValidationFilterAttribute))]
        public IActionResult UpdateSalesInvoice([FromBody] Dto.Sales.SalesInvoice salesInvoiceDto)
        {
            EnsureSalesInvoiceReferencesOwned(salesInvoiceDto);
            Core.Domain.Error.Result<Dto.Sales.SalesInvoice> result;
            try
            {
                using var tx = BeginInvoiceNumberingTransaction();
                result = _salesService.UpdateSalesInvoice(salesInvoiceDto);
                if (result.IsSuccess) tx.Commit();
            }
            catch (Exception ex) when (InvoiceNumberProblem(ex) is { } numberProblem)
            {
                return StatusCode(numberProblem.Status, numberProblem.Message);
            }

            if (result.IsFailure)
                return BadRequest(result.Error.Message);

            return Ok(BuildSalesInvoiceDto(salesInvoiceDto.Id));
        }

        // A new workspace has no items, so a typed line must not fall back to a missing or foreign item id.
        private Core.Domain.Items.Item ResolveFreeTextItem(List<Core.Domain.Items.Item> items, string? description) =>
            Api.Service.CatalogDefaults.ResolveFreeTextItem(_inventoryService, _financialService, items, description);

        private int DefaultMeasurementId() => Api.Service.CatalogDefaults.MeasurementId(_inventoryService);

        // SalesService maps these ids straight onto entities, so each must belong to the current workspace first.
        private void EnsureSalesInvoiceReferencesOwned(Dto.Sales.SalesInvoice dto)
        {
            if (dto.Id > 0)
                _tenantGuard.EnsureOwned<Core.Domain.Sales.SalesInvoiceHeader>(dto.Id, nameof(dto.Id));
            _tenantGuard.EnsureOwned<Core.Domain.Sales.Customer>(dto.CustomerId, nameof(dto.CustomerId));
            _tenantGuard.EnsureOwned<Core.Domain.PaymentTerm>(dto.PaymentTermId, nameof(dto.PaymentTermId));
            _tenantGuard.EnsureOwned<Core.Domain.Sales.SalesOrderHeader>(dto.FromSalesOrderId, nameof(dto.FromSalesOrderId));
            _tenantGuard.EnsureOwned<Core.Domain.Sales.SalesDeliveryHeader>(dto.FromSalesDeliveryId, nameof(dto.FromSalesDeliveryId));

            foreach (var line in dto.SalesInvoiceLines ?? new List<Dto.Sales.SalesInvoiceLine>())
            {
                if (line.Id > 0)
                    _tenantGuard.EnsureOwned<Core.Domain.Sales.SalesInvoiceLine>(line.Id, nameof(line.Id));
                _tenantGuard.EnsureOwned<Core.Domain.Items.Item>(line.ItemId, nameof(line.ItemId));
                _tenantGuard.EnsureOwned<Core.Domain.Items.Measurement>(line.MeasurementId, nameof(line.MeasurementId));
                _tenantGuard.EnsureOwned<Core.Domain.TaxSystem.Tax>(line.TaxId, nameof(line.TaxId));
            }
        }

        [HttpDelete("DeleteSalesInvoice")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesCreate)]
        public async Task<IActionResult> DeleteSalesInvoice(int id)
        {
            // Saving an invoice records a sales order behind it; a deleted draft takes the order with it, so the
            // client isn't left with a record that keeps it from being deleted.
            using var tx = _db.Database.BeginTransaction();
            var orderIds = _db.SalesInvoiceLines
                .Where(l => l.SalesInvoiceHeaderId == id && l.SalesOrderLineId != null)
                .Select(l => l.SalesOrderLine.SalesOrderHeaderId)
                .Distinct()
                .ToList();

            var result = await _salesService.DeleteSalesInvoiceAsync(id);

            if (result.IsFailure)
                return BadRequest(result.Error.Message);

            DeleteUninvoicedOrders(orderIds);
            tx.Commit();
            return NoContent();
        }

        /// <summary>
        /// Deletes the orders none of whose lines are on an invoice any more. Lines are saved before their order, the
        /// order D1 replays them in, since it enforces foreign keys.
        /// </summary>
        private void DeleteUninvoicedOrders(List<int> orderIds)
        {
            var orders = _db.SalesOrderHeaders
                .Where(o => orderIds.Contains(o.Id)
                    && !_db.SalesInvoiceLines.Any(l => l.SalesOrderLine != null && l.SalesOrderLine.SalesOrderHeaderId == o.Id))
                .ToList();
            if (orders.Count == 0)
                return;

            var ids = orders.Select(o => o.Id).ToList();
            _db.SalesOrderLines.RemoveRange(_db.SalesOrderLines.Where(l => ids.Contains(l.SalesOrderHeaderId)));
            _db.SaveChanges();
            _db.SalesOrderHeaders.RemoveRange(orders);
            _db.SaveChanges();
        }

        [HttpPost]
        [Route("RecordPayment")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesReceiptsCreate)]
        public IActionResult RecordPayment([FromBody] RecordInvoicePaymentDto paymentDto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var invoice = _tenantGuard.EnsureOwned<Core.Domain.Sales.SalesInvoiceHeader>(paymentDto.InvoiceId, nameof(paymentDto.InvoiceId));
                invoice = _salesService.GetSalesInvoiceById(invoice.Id);
                if (invoice.Status == Core.Domain.SalesInvoiceStatus.Draft)
                    return BadRequest(new { message = "Issue the invoice before recording a payment." });
                if (invoice.Status == Core.Domain.SalesInvoiceStatus.Void)
                    return BadRequest(new { message = "Cannot record a payment on a void invoice." });

                // Summary accounts such as 10110 can't take postings, so only postable cash accounts qualify.
                var postable = _financialService.GetAccounts().Where(a => a.CanPost()).ToList();
                var bankAccountId = _financialService.GetCashAndBanks()
                    .Select(b => b.AccountId)
                    .FirstOrDefault(id => id.HasValue && postable.Any(a => a.Id == id));
                var cashAccount = postable
                    .Where(a => a.IsCash || (a.ParentAccount != null && a.ParentAccount.IsCash))
                    .OrderBy(a => a.AccountCode)
                    .FirstOrDefault();
                int accountToDebitId = bankAccountId ?? cashAccount?.Id
                    ?? throw new Exception("No cash or bank account is set up to receive payments.");

                int accountToCreditId = invoice.Customer?.AccountsReceivableAccountId ?? 
                    _financialService.GetAccountByAccountCode("10120")?.Id ?? 
                    _financialService.GetAccounts().FirstOrDefault(a => a.AccountClassId == 1)?.Id ?? 1;

                // Auto-post invoice first if unposted, guaranteeing double-entry integrity (DR AR, CR Revenue)
                if (!invoice.GeneralLedgerHeaderId.HasValue)
                {
                    TryPostSalesInvoice(invoice.Id);
                    invoice = _salesService.GetSalesInvoiceById(invoice.Id);
                }

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

                // Update invoice status based on total allocations including taxes and shipping
                decimal totalAllocated = invoice.CustomerAllocations != null ? Math.Round(invoice.CustomerAllocations.Sum(a => a.Amount), 2, MidpointRounding.AwayFromZero) : 0;
                decimal grandTotal = invoice.GrandTotal();
                if (totalAllocated >= grandTotal - 0.005m)
                {
                    invoice.Status = Core.Domain.SalesInvoiceStatus.Closed;
                }
                else
                {
                    invoice.Status = Core.Domain.SalesInvoiceStatus.Open;
                }
                CaptureIfIssued(invoice);
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
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesPost)]
        public IActionResult VoidSalesInvoice(int id)
        {
            try
            {
                using var tx = BeginInvoiceNumberingTransaction();
                var invoice = _salesService.GetSalesInvoiceById(id);
                if (invoice == null)
                    return NotFound(new { message = $"Invoice #{id} not found." });
                if (invoice.Status == Core.Domain.SalesInvoiceStatus.Void)
                    return Ok(new { success = true, invoiceId = invoice.Id, status = "Void" });

                decimal allocated = invoice.CustomerAllocations != null ? invoice.CustomerAllocations.Sum(a => a.Amount) : 0;
                if (allocated > 0)
                {
                    return BadRequest(new { message = "Cannot void an invoice with recorded payments. Please unallocate or refund payments first." });
                }

                // Issuing posted the invoice to receivables and sales; voiding reverses that, so it no longer counts.
                if (invoice.GeneralLedgerHeaderId.HasValue && !invoice.VoidGeneralLedgerHeaderId.HasValue)
                    invoice.VoidGeneralLedgerHeader = _financialService.CreateReversingEntry(invoice.GeneralLedgerHeaderId.Value, $"Void of invoice {invoice.No ?? invoice.Id.ToString()}");
                _salesService.VoidSalesInvoiceStock(invoice);
                invoice.Status = Core.Domain.SalesInvoiceStatus.Void;
                _salesService.UpdateSalesInvoice(invoice);
                tx.Commit();

                return Ok(new { success = true, invoiceId = invoice.Id, status = "Void" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to void invoice {InvoiceId}", id);
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost]
        [Route("IssueSalesInvoice")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesPost)]
        public IActionResult IssueSalesInvoice(int id, string? expectedNo = null)
        {
            try
            {
                using var tx = BeginInvoiceNumberingTransaction();
                var invoice = _salesService.GetSalesInvoiceById(id);
                if (invoice == null)
                    return NotFound(new { message = $"Invoice #{id} not found." });

                if (invoice.Status == Core.Domain.SalesInvoiceStatus.Void)
                    return BadRequest(new { message = "Cannot issue a void invoice." });

                var stockWarnings = invoice.GeneralLedgerHeaderId.HasValue
                    ? new List<string>()
                    : _salesService.GetSalesInvoiceStockWarnings(invoice);

                // The user confirmed the number they were shown; refuse rather than issue under a different one.
                var expected = Core.Domain.Sales.InvoiceNumber.Normalize(expectedNo);
                if (expected != null && Core.Domain.Sales.InvoiceNumber.Normalize(invoice.No) == null)
                {
                    var next = _salesService.PeekNextInvoiceNumber();
                    if (!string.Equals(next, expected, StringComparison.OrdinalIgnoreCase))
                        return Conflict(new { message = $"Another invoice just took #{expected}. This one will be #{next}.", code = "number_changed", next });
                }

                invoice.Status = Core.Domain.SalesInvoiceStatus.Open;
                CaptureIfIssued(invoice);
                _salesService.UpdateSalesInvoice(invoice);
                tx.Commit();

                if (!invoice.GeneralLedgerHeaderId.HasValue)
                {
                    var postingError = TryPostSalesInvoice(invoice.Id);
                    if (postingError != null)
                        stockWarnings.Add(postingError);
                }

                return Ok(new { success = true, invoiceId = invoice.Id, no = invoice.No, status = "Open", warnings = stockWarnings });
            }
            catch (Exception ex)
            {
                if (InvoiceNumberProblem(ex) is { } numberProblem)
                    return StatusCode(numberProblem.Status, new { message = numberProblem.Message, code = numberProblem.Code });
                _logger.LogError(ex, "Failed to issue invoice {InvoiceId}", id);
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost]
        [Route("SaveQuotation")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesCreate)]
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

                _tenantGuard.EnsureOwned<Core.Domain.Sales.Customer>(quotationDto.CustomerId.GetValueOrDefault(), nameof(quotationDto.CustomerId));
                if (quotationDto.PaymentTermId > 0)
                    _tenantGuard.EnsureOwned<Core.Domain.PaymentTerm>(quotationDto.PaymentTermId, nameof(quotationDto.PaymentTermId));

                if (isNew)
                {
                    salesQuote = new Core.Domain.Sales.SalesQuoteHeader();
                    salesQuote.Status = SalesQuoteStatus.Draft;
                }
                else
                {
                    salesQuote = _tenantGuard.EnsureOwned<Core.Domain.Sales.SalesQuoteHeader>(quotationDto.Id, nameof(quotationDto.Id));
                    salesQuote.Status = (SalesQuoteStatus)quotationDto.StatusId;
                }

                salesQuote.CustomerId = quotationDto.CustomerId.GetValueOrDefault();
                salesQuote.Date = quotationDto.QuotationDate;

                salesQuote.ReferenceNo = quotationDto.ReferenceNo;
                salesQuote.PaymentTermId = quotationDto.PaymentTermId;

                foreach (var line in quotationDto.SalesQuotationLines)
                {
                    if (line.ItemId.HasValue && line.ItemId.Value > 0)
                        _tenantGuard.EnsureOwned<Core.Domain.Items.Item>(line.ItemId.Value, nameof(line.ItemId));
                    if (line.MeasurementId.HasValue && line.MeasurementId.Value > 0)
                        _tenantGuard.EnsureOwned<Core.Domain.Items.Measurement>(line.MeasurementId.Value, nameof(line.MeasurementId));

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
                if (ex is Core.Tenancy.TenantException) throw;
                errors = new string[1] { ex.InnerException != null ? ex.InnerException.Message : ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }

        [HttpPost]
        [Route("SaveReceipt")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesReceiptsCreate)]
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

                var bank = _tenantGuard.EnsureOwned<Core.Domain.Financials.Bank>((int)receiptDto.AccountToDebitId, "AccountToDebitId");
                var customer = _tenantGuard.EnsureOwned<Core.Domain.Sales.Customer>((int)receiptDto.CustomerId, "CustomerId");
                _tenantGuard.EnsureOwned<Core.Domain.Financials.Account>((int)receiptDto.AccountToCreditId, "AccountToCreditId");

                var salesReceipt = new Core.Domain.Sales.SalesReceiptHeader();
                salesReceipt.Date = receiptDto.ReceiptDate;
                salesReceipt.CustomerId = receiptDto.CustomerId;
                salesReceipt.AccountToDebitId = bank.AccountId;
                salesReceipt.Amount = receiptDto.Amount;
                salesReceipt.PaymentMethod = receiptDto.PaymentMethod != null ? (string)receiptDto.PaymentMethod : null;
                salesReceipt.ReferenceNo = receiptDto.ReferenceNo != null ? (string)receiptDto.ReferenceNo : null;
                salesReceipt.Memo = receiptDto.Memo != null ? (string)receiptDto.Memo : null;

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
                if (ex is Core.Tenancy.TenantException) throw;
                errors = new string[1] { ex.InnerException != null ? ex.InnerException.Message : ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }

        [HttpPost]
        [Route("UpdateReceipt")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesReceiptsCreate)]
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
                if (ex is Core.Tenancy.TenantException) throw;
                errors = new string[1] { ex.InnerException != null ? ex.InnerException.Message : ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }

        [HttpPost]
        [Route("SaveAllocation")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesReceiptsCreate)]
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

                _tenantGuard.EnsureOwned<Core.Domain.Sales.Customer>((int)allocationDto.CustomerId, "CustomerId");
                _tenantGuard.EnsureOwned<Core.Domain.Sales.SalesReceiptHeader>((int)allocationDto.ReceiptId, "ReceiptId");

                foreach (var line in allocationDto.AllocationLines)
                {
                    _tenantGuard.EnsureOwned<Core.Domain.Sales.SalesInvoiceHeader>((int)line.InvoiceId, "InvoiceId");
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
                if (ex is Core.Tenancy.TenantException) throw;
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
            var invoice = BuildSalesInvoiceDto(id);
            return invoice == null
                ? NotFound(new { message = $"Invoice #{id} not found." })
                : Ok(invoice);
        }

        [HttpPost]
        [Route("BookQuotation")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesCreate)]
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