using Api.Filters;
using Core.Domain;
using Core.Domain.Purchases;
using Core.Domain.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Services.Administration;
using Services.Banking;
using Services.Financial;
using Services.Inventory;
using Services.Purchasing;

namespace Api.Controllers
{
    /// <summary>
    /// Purchases &amp; Bills: the vendors a workspace buys from, the bills they send, and the payments made on them. A
    /// bill is drafted, then approved, which posts it to Accounts Payable; paid from a cash or bank account; or voided,
    /// which reverses what approving posted. The purchase-order endpoints are the original ones, kept as they were.
    /// </summary>
    [Core.Tenancy.TenantScoped]
    [Route("api/[controller]")]
    [RequireModule("purchases")]
    public class PurchasingController : BaseController
    {
        private const int VendorNameMaxLength = 200;
        private const int VendorInvoiceNoMaxLength = 50;
        private const int NotesMaxLength = 2000;

        private readonly IAdministrationService _adminService;
        private readonly IPurchasingService _purchasingService;
        private readonly IFinancialService _financialService;
        private readonly IInventoryService _inventoryService;
        private readonly IBankAccountService _bankAccounts;
        private readonly Core.Tenancy.ITenantGuard _tenantGuard;
        private readonly Core.Tenancy.ITenantContext _tenantContext;
        private readonly Api.Data.ApiDbContext _db;
        private readonly ILogger<PurchasingController> _logger;

        public PurchasingController(IAdministrationService adminService,
            IPurchasingService purchasingService,
            IFinancialService financialService,
            IInventoryService inventoryService,
            IBankAccountService bankAccounts,
            Core.Tenancy.ITenantGuard tenantGuard,
            Core.Tenancy.ITenantContext tenantContext,
            Api.Data.ApiDbContext db,
            ILogger<PurchasingController> logger)
        {
            _adminService = adminService;
            _purchasingService = purchasingService;
            _financialService = financialService;
            _inventoryService = inventoryService;
            _bankAccounts = bankAccounts;
            _tenantGuard = tenantGuard;
            _tenantContext = tenantContext;
            _db = db;
            _logger = logger;
        }

        [HttpGet]
        [Route("PurchaseOrders")]
        [RequirePermission(SystemPermissions.PurchasesOrdersView)]
        public IActionResult PurchaseOrders()
        {
            var purchaseOrders = _purchasingService.GetPurchaseOrders();
            IList<Dto.Purchasing.PurchaseOrder> purchaseOrdersDto = new List<Dto.Purchasing.PurchaseOrder>();

            foreach (var purchaseOrder in purchaseOrders)
            {
                var purchaseOrderDto = new Dto.Purchasing.PurchaseOrder()
                {
                    Id = purchaseOrder.Id,
                    No = purchaseOrder.No,
                    VendorId = purchaseOrder.VendorId!.Value,
                    VendorName = purchaseOrder.Vendor.Party.Name,
                    OrderDate = purchaseOrder.Date,
                    ReferenceNo = purchaseOrder.ReferenceNo,
                    StatusId = (int)purchaseOrder.Status.GetValueOrDefault()
                };

                foreach (var line in purchaseOrder.PurchaseOrderLines)
                {
                    var lineDto = new Dto.Purchasing.PurchaseOrderLine()
                    {
                        ItemId = line.ItemId,
                        TaxGroupId = line.TaxGroupId,
                        Quantity = line.Quantity,
                        Amount = line.Amount,
                        Discount = line.Discount,
                        RemainingQtyToInvoice = line.GetRemainingQtyToInvoice(),
                    };
                    purchaseOrderDto.PurchaseOrderLines.Add(lineDto);
                }

                purchaseOrdersDto.Add(purchaseOrderDto);
            }

            return new ObjectResult(purchaseOrdersDto);
        }

        [HttpGet]
        [Route("PurchaseOrder")]
        [RequirePermission(SystemPermissions.PurchasesOrdersView)]
        public IActionResult PurchaseOrder(int id)
        {
            var purchaseOrder = _purchasingService.GetPurchaseOrderById(id);

            // Bug 7 fix: guard against invalid ID
            if (purchaseOrder == null)
                return NotFound($"Purchase order with ID {id} was not found.");

            Dto.Purchasing.PurchaseOrder purchaseOrderDto;

            purchaseOrderDto = new Dto.Purchasing.PurchaseOrder()
            {
                Id = purchaseOrder.Id,
                VendorId = purchaseOrder.VendorId!.Value,
                VendorName = purchaseOrder.Vendor.Party.Name,
                OrderDate = purchaseOrder.Date,
                PaymentTermId = purchaseOrder.PaymentTermId,
                ReferenceNo = purchaseOrder.ReferenceNo,
                StatusId = (int)purchaseOrder.Status.GetValueOrDefault()
            };

            foreach (var item in purchaseOrder.PurchaseOrderLines)
            {
                var line = new Dto.Purchasing.PurchaseOrderLine()
                {
                    Id = item.Id,
                    ItemId = item.ItemId,
                    TaxGroupId = item.TaxGroupId,
                    Quantity = item.Quantity,
                    Amount = item.Amount,
                    Discount = item.Discount,
                    RemainingQtyToInvoice = item.GetRemainingQtyToInvoice(),
                };

                purchaseOrderDto.PurchaseOrderLines.Add(line);
            }

            return new ObjectResult(purchaseOrderDto);
        }

        [HttpPost]
        [Route("savepurchaseorder")]
        [RequirePermission(SystemPermissions.PurchasesBillsCreate)]
        public IActionResult SavePurchaseOrder([FromBody] Dto.Purchasing.PurchaseOrder purchaseOrderDto)
        {
            string[]? errors = null;

            if (!ModelState.IsValid)
            {
                // Bug 6 fix: use an index counter to avoid IndexOutOfRange across Values/Errors
                errors = new string[ModelState.ErrorCount];
                int idx = 0;
                foreach (var val in ModelState.Values)
                    foreach (var err in val.Errors)
                        if (idx < errors.Length)
                            errors[idx++] = err.ErrorMessage;

                return new BadRequestObjectResult(errors);
            }

            try
            {
                bool isNew = purchaseOrderDto.Id == 0;
                Core.Domain.Purchases.PurchaseOrderHeader? purchaseOrder = null;

                _tenantGuard.EnsureOwned<Core.Domain.Purchases.Vendor>(purchaseOrderDto.VendorId, nameof(purchaseOrderDto.VendorId));
                if (purchaseOrderDto.PaymentTermId.HasValue && purchaseOrderDto.PaymentTermId.Value > 0)
                    _tenantGuard.EnsureOwned<Core.Domain.PaymentTerm>(purchaseOrderDto.PaymentTermId.Value, nameof(purchaseOrderDto.PaymentTermId));

                if (isNew)
                {
                    purchaseOrder = new Core.Domain.Purchases.PurchaseOrderHeader();
                }
                else
                {
                    purchaseOrder = _tenantGuard.EnsureOwned<Core.Domain.Purchases.PurchaseOrderHeader>(purchaseOrderDto.Id, nameof(purchaseOrderDto.Id));
                }
                purchaseOrder.ReferenceNo = purchaseOrderDto.ReferenceNo;
                purchaseOrder.PaymentTermId = purchaseOrderDto.PaymentTermId;
                purchaseOrder.VendorId = purchaseOrderDto.VendorId;
                purchaseOrder.Date = purchaseOrderDto.OrderDate;

                foreach (var line in purchaseOrderDto.PurchaseOrderLines)
                {
                    if (line.ItemId > 0)
                        _tenantGuard.EnsureOwned<Core.Domain.Items.Item>(line.ItemId, nameof(line.ItemId));
                    if (line.TaxGroupId.HasValue && line.TaxGroupId.Value > 0)
                        _tenantGuard.EnsureOwned<Core.Domain.TaxSystem.TaxGroup>(line.TaxGroupId.Value, nameof(line.TaxGroupId));

                    if (!isNew)
                    {
                        var existingLine = purchaseOrder.PurchaseOrderLines.Where(id => id.Id == line.Id).FirstOrDefault();
                        if (existingLine != null)
                        {
                            existingLine.Amount = line.Amount.GetValueOrDefault();
                            existingLine.Discount = line.Discount.GetValueOrDefault();
                            existingLine.Quantity = line.Quantity.GetValueOrDefault();
                            existingLine.ItemId = line.ItemId.GetValueOrDefault();
                            existingLine.TaxGroupId = line.TaxGroupId;
                        }
                        else
                        {
                            var purchaseOrderLine = new Core.Domain.Purchases.PurchaseOrderLine();
                            purchaseOrderLine.Amount = line.Amount.GetValueOrDefault();
                            purchaseOrderLine.Discount = line.Discount.GetValueOrDefault();
                            purchaseOrderLine.Quantity = line.Quantity.GetValueOrDefault();
                            purchaseOrderLine.ItemId = line.ItemId.GetValueOrDefault();
                            purchaseOrderLine.TaxGroupId = line.TaxGroupId;

                            purchaseOrder.PurchaseOrderLines.Add(purchaseOrderLine);
                        }
                    }
                    else
                    {
                        var purchaseOrderLine = new Core.Domain.Purchases.PurchaseOrderLine();
                        purchaseOrderLine.Amount = line.Amount.GetValueOrDefault();
                        purchaseOrderLine.Discount = line.Discount.GetValueOrDefault();
                        purchaseOrderLine.Quantity = line.Quantity.GetValueOrDefault();
                        purchaseOrderLine.ItemId = line.ItemId.GetValueOrDefault();
                        purchaseOrderLine.TaxGroupId = line.TaxGroupId;

                        purchaseOrder.PurchaseOrderLines.Add(purchaseOrderLine);
                    }
                }

                if (isNew)
                {
                    _purchasingService.AddPurchaseOrder(purchaseOrder, true);
                }
                else
                {
                    var deleted = (from line in purchaseOrder.PurchaseOrderLines
                                   where !purchaseOrderDto.PurchaseOrderLines.Any(x => x.Id == line.Id)
                                   select line).ToList();

                    foreach (var line in deleted)
                    {
                        if (line.PurchaseInvoiceLines.Count() > 0)
                            throw new Exception("The line cannot be deleted. An invoice line is created from the item.");
                    }

                    foreach (var line in deleted)
                    {
                        purchaseOrder.PurchaseOrderLines.Remove(line);
                    }

                    _purchasingService.UpdatePurchaseOrder(purchaseOrder);
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


        // Vendors: the Purchases counterpart of Sales' clients. Saving a vendor changes only the details below; bills
        // already approved keep the vendor they were approved with.

        [HttpGet]
        [Route("Vendors")] // api/Purchasing/Vendors
        [RequirePermission(SystemPermissions.PurchasesVendorsView)]
        public IActionResult Vendors()
        {
            var vendors = _db.Vendors.Include(v => v.Party)
                .Where(v => v.Party != null)
                .OrderBy(v => v.Party.Name)
                .AsEnumerable()
                .Select(v => ToSupplier(v))
                .ToList();
            AddBalances(vendors, vendorId: null);
            return Ok(vendors);
        }

        [HttpGet]
        [Route("Vendor")] // api/Purchasing/Vendor?id=
        [RequirePermission(SystemPermissions.PurchasesVendorsView)]
        public IActionResult Vendor(int id)
        {
            var vendor = _db.Vendors.Include(v => v.Party).FirstOrDefault(v => v.Id == id && v.Party != null);
            if (vendor == null)
                return NotFound(new { message = "Vendor not found." });

            var supplier = ToSupplier(vendor, LoadVendorContacts(vendor));
            supplier.CanDelete = !VendorHasRecords(id);
            AddBalances(new[] { supplier }, id);
            return Ok(supplier);
        }

        [HttpPost]
        [Route("SaveVendor")]
        [RequirePermission(SystemPermissions.PurchasesVendorsManage)]
        public IActionResult SaveVendor([FromBody] Dto.Purchasing.Supplier supplierDto)
        {
            var name = Blank(supplierDto.Name);
            if (name == null)
                return BadRequest(new { message = "Enter the vendor's name." });
            if (name.Length > VendorNameMaxLength)
                return BadRequest(new { message = $"A vendor name can be at most {VendorNameMaxLength} characters." });
            var email = Blank(supplierDto.Email);
            if (email != null && !ValidEmail(email))
                return BadRequest(new { message = "Enter a valid email address, or leave it blank." });
            int? paymentTermId = supplierDto.PaymentTermId > 0 ? supplierDto.PaymentTermId : null;
            if (paymentTermId.HasValue)
                _tenantGuard.EnsureOwned<PaymentTerm>(paymentTermId.Value, nameof(supplierDto.PaymentTermId));

            Vendor vendor;
            bool isNew = supplierDto.Id == 0;
            if (isNew)
            {
                var contactName = name.Split(' ', 2);
                vendor = new Vendor
                {
                    Party = new Party { PartyType = PartyTypes.Vendor, IsActive = true },
                    PrimaryContact = new Contact
                    {
                        ContactType = ContactTypes.Vendor,
                        FirstName = contactName[0],
                        LastName = contactName.Length > 1 ? contactName[1] : "Contact",
                        Party = new Party { PartyType = PartyTypes.Contact, Name = name, Email = email }
                    }
                };
            }
            else
            {
                _tenantGuard.EnsureOwned<Vendor>(supplierDto.Id, nameof(supplierDto.Id));
                vendor = _db.Vendors.Include(v => v.Party).Single(v => v.Id == supplierDto.Id);
            }

            // The address lives on the party, as a client's does; the vendor's own address fields follow it.
            vendor.Party.Name = name;
            vendor.Party.Email = email;
            vendor.Party.Phone = Blank(supplierDto.Phone);
            vendor.Party.CompanyStreet = vendor.Street1 = Blank(supplierDto.Street);
            vendor.Party.CompanyProvince = vendor.City = Blank(supplierDto.City);
            vendor.Party.CompanyPostalCode = vendor.PostalCode = Blank(supplierDto.PostalCode);
            vendor.Party.CompanyCountry = vendor.Country = Blank(supplierDto.Country);
            vendor.PaymentTermId = paymentTermId;
            vendor.ModifiedBy = GetUserNameFromRequestHeader();

            if (isNew)
            {
                _purchasingService.AddVendor(vendor);
                EnsurePrimaryContactLinked(vendor);
            }
            else
                _purchasingService.UpdateVendor(vendor);

            var saved = ToSupplier(vendor, LoadVendorContacts(vendor));
            saved.CanDelete = isNew || !VendorHasRecords(vendor.Id);
            if (!isNew)
                AddBalances(new[] { saved }, vendor.Id);
            return Ok(saved);
        }

        [HttpPost]
        [Route("VendorContact")]
        [RequirePermission(SystemPermissions.PurchasesVendorsManage)]
        public IActionResult VendorContact(int vendorId, [FromBody] Dto.Common.ContactPersonDto contactDto)
        {
            _tenantGuard.EnsureOwned<Vendor>(vendorId, nameof(vendorId));
            var vendor = _db.Vendors.Include(v => v.Party).Single(v => v.Id == vendorId);
            var firstName = Blank(contactDto.FirstName);
            var lastName = Blank(contactDto.LastName);
            if (firstName == null || lastName == null)
                return BadRequest(new { message = "Enter the contact's first and last name." });
            var email = Blank(contactDto.Email);
            if (email != null && !ValidEmail(email))
                return BadRequest(new { message = "Enter a valid email address, or leave it blank." });

            Contact contact;
            var isNewContact = contactDto.Id <= 0;
            if (!isNewContact)
            {
                _tenantGuard.EnsureOwned<Contact>(contactDto.Id, nameof(contactDto.Id));
                if (!_db.Set<VendorContact>().Any(vc => vc.VendorId == vendorId && vc.ContactId == contactDto.Id)
                    && vendor.PrimaryContactId != contactDto.Id)
                    return BadRequest(new { message = "That contact is not linked to this vendor." });
                contact = _db.Contacts.Include(c => c.Party).Single(c => c.Id == contactDto.Id);
            }
            else
            {
                contact = new Contact
                {
                    ContactType = ContactTypes.Vendor,
                    Party = new Party { PartyType = PartyTypes.Contact, IsActive = true },
                };
                _db.Contacts.Add(contact);
                _db.Set<VendorContact>().Add(new VendorContact
                {
                    VendorId = vendorId,
                    Contact = contact,
                });
            }

            contact.FirstName = firstName;
            contact.LastName = lastName;
            contact.MiddleName = Blank(contactDto.MiddleName);
            contact.Party.Name = $"{firstName} {lastName}".Trim();
            contact.Party.Email = email;
            contact.Party.Phone = Blank(contactDto.Phone);
            contact.Party.CompanyStreet = Blank(contactDto.Street);
            contact.Party.CompanyProvince = Blank(contactDto.City);
            contact.Party.CompanyPostalCode = Blank(contactDto.PostalCode);
            contact.Party.CompanyCountry = Blank(contactDto.Country);
            contact.ModifiedBy = GetUserNameFromRequestHeader();

            if (contactDto.IsPrimary || vendor.PrimaryContactId == null)
                vendor.PrimaryContact = contact;
            if (contactDto.IsBillTo)
                vendor.BillToContact = contact;
            else if (vendor.BillToContactId == contact.Id)
                vendor.BillToContactId = null;
            vendor.ModifiedBy = GetUserNameFromRequestHeader();
            _db.SaveChanges();

            var supplier = ToSupplier(vendor, LoadVendorContacts(vendor));
            supplier.CanDelete = !VendorHasRecords(vendorId);
            AddBalances(new[] { supplier }, vendorId);
            return Ok(supplier);
        }

        [HttpPost]
        [Route("SetVendorBillToContact")]
        [RequirePermission(SystemPermissions.PurchasesVendorsManage)]
        public IActionResult SetVendorBillToContact(int vendorId, int contactId)
        {
            _tenantGuard.EnsureOwned<Vendor>(vendorId, nameof(vendorId));
            _tenantGuard.EnsureOwned<Contact>(contactId, nameof(contactId));
            var vendor = _db.Vendors.Include(v => v.Party).Single(v => v.Id == vendorId);
            if (!_db.Set<VendorContact>().Any(vc => vc.VendorId == vendorId && vc.ContactId == contactId)
                && vendor.PrimaryContactId != contactId)
                return BadRequest(new { message = "That contact is not linked to this vendor." });

            vendor.BillToContactId = contactId;
            vendor.ModifiedBy = GetUserNameFromRequestHeader();
            _db.SaveChanges();

            var supplier = ToSupplier(vendor, LoadVendorContacts(vendor));
            supplier.CanDelete = !VendorHasRecords(vendorId);
            AddBalances(new[] { supplier }, vendorId);
            return Ok(supplier);
        }

        [HttpPost]
        [Route("SetVendorPrimaryContact")]
        [RequirePermission(SystemPermissions.PurchasesVendorsManage)]
        public IActionResult SetVendorPrimaryContact(int vendorId, int contactId)
        {
            _tenantGuard.EnsureOwned<Vendor>(vendorId, nameof(vendorId));
            _tenantGuard.EnsureOwned<Contact>(contactId, nameof(contactId));
            var vendor = _db.Vendors.Include(v => v.Party).Single(v => v.Id == vendorId);
            if (!_db.Set<VendorContact>().Any(vc => vc.VendorId == vendorId && vc.ContactId == contactId)
                && vendor.PrimaryContactId != contactId)
                return BadRequest(new { message = "That contact is not linked to this vendor." });

            vendor.PrimaryContactId = contactId;
            vendor.ModifiedBy = GetUserNameFromRequestHeader();
            _db.SaveChanges();

            var supplier = ToSupplier(vendor, LoadVendorContacts(vendor));
            supplier.CanDelete = !VendorHasRecords(vendorId);
            AddBalances(new[] { supplier }, vendorId);
            return Ok(supplier);
        }

        [HttpDelete]
        [Route("DeleteVendorContact")]
        [RequirePermission(SystemPermissions.PurchasesVendorsManage)]
        public IActionResult DeleteVendorContact(int vendorId, int contactId)
        {
            _tenantGuard.EnsureOwned<Vendor>(vendorId, nameof(vendorId));
            _tenantGuard.EnsureOwned<Contact>(contactId, nameof(contactId));
            var vendor = _db.Vendors.Include(v => v.Party).Single(v => v.Id == vendorId);
            if (vendor.PrimaryContactId == contactId)
                return BadRequest(new { message = "Set another contact as primary before removing this one." });

            var links = _db.Set<VendorContact>().Where(vc => vc.VendorId == vendorId && vc.ContactId == contactId).ToList();
            if (links.Count == 0 && vendor.PrimaryContactId != contactId)
                return NotFound(new { message = "Contact not found on this vendor." });

            using var tx = _db.Database.BeginTransaction();
            _db.RemoveRange(links);
            if (vendor.BillToContactId == contactId)
                vendor.BillToContactId = null;
            vendor.ModifiedBy = GetUserNameFromRequestHeader();
            _db.SaveChanges();

            var stillLinked = _db.Set<VendorContact>().Any(vc => vc.ContactId == contactId)
                || _db.Set<CustomerContact>().Any(cc => cc.ContactId == contactId)
                || _db.Vendors.Any(v => v.PrimaryContactId == contactId || v.BillToContactId == contactId)
                || _db.Customers.Any(c => c.PrimaryContactId == contactId || c.BillToContactId == contactId);
            if (!stillLinked)
            {
                var contact = _db.Contacts.Include(c => c.Party).FirstOrDefault(c => c.Id == contactId);
                if (contact != null)
                {
                    var partyId = contact.PartyId;
                    _db.Contacts.Remove(contact);
                    _db.SaveChanges();
                    if (!_db.Customers.Any(c => c.PartyId == partyId)
                        && !_db.Vendors.Any(v => v.PartyId == partyId)
                        && !_db.Contacts.Any(c => c.PartyId == partyId))
                    {
                        var party = _db.Set<Party>().FirstOrDefault(p => p.Id == partyId);
                        if (party != null)
                            _db.Set<Party>().Remove(party);
                        _db.SaveChanges();
                    }
                }
            }
            tx.Commit();

            var supplier = ToSupplier(vendor, LoadVendorContacts(vendor));
            supplier.CanDelete = !VendorHasRecords(vendorId);
            AddBalances(new[] { supplier }, vendorId);
            return Ok(supplier);
        }

        [HttpPost]
        [Route("ArchiveVendor")]
        [RequirePermission(SystemPermissions.PurchasesVendorsManage)]
        public IActionResult ArchiveVendor(int id, bool archived = true)
        {
            _tenantGuard.EnsureOwned<Vendor>(id, nameof(id));
            var vendor = _db.Vendors.Include(v => v.Party).Single(v => v.Id == id);
            vendor.ArchivedAt = archived ? vendor.ArchivedAt ?? DateTime.UtcNow : null;
            vendor.ModifiedBy = GetUserNameFromRequestHeader();
            _purchasingService.UpdateVendor(vendor);

            var supplier = ToSupplier(vendor, LoadVendorContacts(vendor));
            supplier.CanDelete = !VendorHasRecords(id);
            AddBalances(new[] { supplier }, id);
            return Ok(supplier);
        }

        /// <summary>
        /// Deletes a vendor nothing has been recorded for, such as one added by mistake. A vendor with bills, orders or
        /// payments is archived instead, so its history stays intact.
        /// </summary>
        [HttpDelete]
        [Route("DeleteVendor")]
        [RequirePermission(SystemPermissions.PurchasesVendorsManage)]
        public IActionResult DeleteVendor(int id)
        {
            _tenantGuard.EnsureOwned<Vendor>(id, nameof(id));
            if (VendorHasRecords(id))
                return BadRequest(new { message = "This vendor has bills or payments, so it can't be deleted. Archive it instead." });

            var vendor = _db.Vendors.Single(v => v.Id == id);
            var contact = vendor.PrimaryContactId.HasValue ? _db.Contacts.FirstOrDefault(c => c.Id == vendor.PrimaryContactId) : null;
            var parties = new[] { vendor.PartyId, contact?.PartyId }
                .Where(partyId => partyId.HasValue)
                .Select(partyId => partyId!.Value)
                .Distinct()
                .ToList();

            // Saved in foreign-key order: D1 replays the statements in order and enforces the keys.
            using var tx = _db.Database.BeginTransaction();
            _db.RemoveRange(_db.Set<VendorContact>().Where(vc => vc.VendorId == id));
            vendor.BillToContactId = null;
            vendor.PrimaryContactId = null;
            _db.SaveChanges();
            _db.Vendors.Remove(vendor);
            _db.SaveChanges();
            if (contact != null
                && !_db.Vendors.Any(v => v.PrimaryContactId == contact.Id || v.BillToContactId == contact.Id)
                && !_db.Customers.Any(c => c.PrimaryContactId == contact.Id || c.BillToContactId == contact.Id))
            {
                _db.RemoveRange(_db.Set<VendorContact>().Where(vc => vc.ContactId == contact.Id));
                _db.RemoveRange(_db.Set<CustomerContact>().Where(cc => cc.ContactId == contact.Id));
                _db.Contacts.Remove(contact);
                _db.SaveChanges();
            }
            // A party still used by another role (say, the same business as a client) stays.
            _db.Set<Party>().RemoveRange(_db.Set<Party>().Where(p => parties.Contains(p.Id)
                && !_db.Customers.Any(c => c.PartyId == p.Id)
                && !_db.Vendors.Any(v => v.PartyId == p.Id)
                && !_db.Contacts.Any(c => c.PartyId == p.Id)));
            _db.SaveChanges();
            tx.Commit();

            return NoContent();
        }

        /// <summary>
        /// What the workspace owes each vendor, from the same bill totals and statuses the bill pages show. Drafts and
        /// voided bills are not owed.
        /// </summary>
        private void AddBalances(IEnumerable<Dto.Purchasing.Supplier> vendors, int? vendorId)
        {
            var byId = vendors.ToDictionary(v => v.Id);
            var terms = _adminService.GetPaymentTerms().ToList();
            var today = DateTime.UtcNow;
            foreach (var bill in BillsQuery().Where(b => vendorId == null || b.VendorId == vendorId).AsEnumerable())
            {
                if (!bill.IsPayable || bill.VendorId == null || !byId.TryGetValue(bill.VendorId.Value, out var vendor))
                    continue;

                var balance = bill.BalanceDue();
                vendor.Outstanding += balance;
                if (bill.DisplayStatus(bill.ResolveDueDate(terms.FirstOrDefault(t => t.Id == bill.PaymentTermId)), today) == "Overdue")
                    vendor.Overdue += balance;
                vendor.PaidToDate += bill.AmountPaid();
                if (vendor.LastBillDate == null || bill.Date > vendor.LastBillDate)
                    vendor.LastBillDate = bill.Date;
            }
        }

        private static Dto.Purchasing.Supplier ToSupplier(
            Vendor vendor,
            List<Dto.Common.ContactPersonDto>? contacts = null) => new()
        {
            Id = vendor.Id,
            Name = vendor.Party?.Name,
            Email = vendor.Party?.Email,
            Phone = vendor.Party?.Phone,
            Street = vendor.Party?.CompanyStreet ?? vendor.Street1,
            City = vendor.Party?.CompanyProvince ?? vendor.City,
            PostalCode = vendor.Party?.CompanyPostalCode ?? vendor.PostalCode,
            Country = vendor.Party?.CompanyCountry ?? vendor.Country,
            PaymentTermId = vendor.PaymentTermId,
            BillToContactId = vendor.BillToContactId,
            Contacts = contacts ?? new List<Dto.Common.ContactPersonDto>(),
            Archived = vendor.ArchivedAt.HasValue,
        };

        private List<Dto.Common.ContactPersonDto> LoadVendorContacts(Vendor vendor)
        {
            var contactIds = _db.Set<VendorContact>()
                .Where(vc => vc.VendorId == vendor.Id)
                .Select(vc => vc.ContactId)
                .ToList();
            if (vendor.PrimaryContactId is int primaryId && !contactIds.Contains(primaryId))
                contactIds.Add(primaryId);
            if (vendor.BillToContactId is int billToId && !contactIds.Contains(billToId))
                contactIds.Add(billToId);

            return _db.Contacts.Include(c => c.Party)
                .Where(c => contactIds.Contains(c.Id))
                .AsEnumerable()
                .Select(c => ToContactPerson(c, vendor.PrimaryContactId, vendor.BillToContactId))
                .OrderByDescending(c => c.IsBillTo)
                .ThenByDescending(c => c.IsPrimary)
                .ThenBy(c => c.FullName)
                .ToList();
        }

        private static Dto.Common.ContactPersonDto ToContactPerson(
            Contact contact, int? primaryContactId, int? billToContactId) => new()
        {
            Id = contact.Id,
            FirstName = contact.FirstName ?? "",
            LastName = contact.LastName ?? "",
            MiddleName = contact.MiddleName,
            Email = contact.Party?.Email,
            Phone = contact.Party?.Phone,
            Street = contact.Party?.CompanyStreet,
            City = contact.Party?.CompanyProvince,
            PostalCode = contact.Party?.CompanyPostalCode,
            Country = contact.Party?.CompanyCountry,
            IsPrimary = contact.Id == primaryContactId,
            IsBillTo = contact.Id == billToContactId,
        };

        private void EnsurePrimaryContactLinked(Vendor vendor)
        {
            if (!vendor.PrimaryContactId.HasValue)
                return;
            if (!_db.Set<VendorContact>().Any(vc => vc.VendorId == vendor.Id && vc.ContactId == vendor.PrimaryContactId))
            {
                _db.Set<VendorContact>().Add(new VendorContact
                {
                    VendorId = vendor.Id,
                    ContactId = vendor.PrimaryContactId.Value,
                });
            }
            _db.SaveChanges();
        }

        /// <summary>Whether anything has been recorded for the vendor: bills (drafts too), orders, receipts or payments.</summary>
        private bool VendorHasRecords(int vendorId) =>
            _db.PurchaseInvoiceHeaders.Any(b => b.VendorId == vendorId)
            || _db.PurchaseOrderHeaders.Any(o => o.VendorId == vendorId)
            || _db.PurchaseReceiptHeaders.Any(r => r.VendorId == vendorId)
            || _db.VendorPayments.Any(p => p.VendorId == vendorId);

        // Bills.

        [HttpGet]
        [Route("Bills")] // api/Purchasing/Bills, or ?vendorId= for one vendor's
        [RequirePermission(SystemPermissions.PurchasesBillsView)]
        public IActionResult Bills(int? vendorId = null)
        {
            var terms = _adminService.GetPaymentTerms().ToList();
            var bills = BillsQuery()
                .Where(b => vendorId == null || b.VendorId == vendorId)
                .OrderByDescending(b => b.Date).ThenByDescending(b => b.Id)
                .AsEnumerable()
                .Select(b => ToBill(b, terms))
                .ToList();
            return Ok(bills);
        }

        [HttpGet]
        [Route("Bill")] // api/Purchasing/Bill?id=
        [RequirePermission(SystemPermissions.PurchasesBillsView)]
        public IActionResult Bill(int id)
        {
            var bill = BillsQuery().FirstOrDefault(b => b.Id == id);
            if (bill == null)
                return NotFound(new { message = "Bill not found." });
            return Ok(ToBill(bill, _adminService.GetPaymentTerms().ToList()));
        }

        /// <summary>
        /// Saves a draft bill, or with status "Open" saves and approves it, which takes the same permission as approving.
        /// An approved bill can't be changed: it is voided and entered again.
        /// </summary>
        [HttpPost]
        [Route("SaveBill")]
        [RequirePermission(SystemPermissions.PurchasesBillsCreate)]
        public IActionResult SaveBill([FromBody] Dto.Purchasing.Bill billDto)
        {
            bool approving = billDto.Status == "Open";
            if (approving && !_tenantContext.Permissions.Contains(SystemPermissions.PurchasesBillsPost))
                return RequirePermissionAttribute.Denied(SystemPermissions.PurchasesBillsPost);

            if (billDto.VendorId <= 0)
                return BadRequest(new { message = "Pick the vendor the bill is from." });
            if (billDto.Lines.Count == 0)
                return BadRequest(new { message = "Add at least one line to the bill." });
            if (billDto.Lines.Any(l => (l.ItemId ?? 0) <= 0 && string.IsNullOrWhiteSpace(l.Description)))
                return BadRequest(new { message = "Describe each line, or pick an item for it." });
            if (billDto.Lines.Any(l => l.Quantity <= 0))
                return BadRequest(new { message = "Each line's quantity must be more than zero." });
            if (billDto.Lines.Any(l => l.Discount is < 0 or > 100))
                return BadRequest(new { message = "A discount is a percentage from 0 to 100." });
            var vendorInvoiceNo = Blank(billDto.VendorInvoiceNo);
            if (vendorInvoiceNo?.Length > VendorInvoiceNoMaxLength)
                return BadRequest(new { message = $"The vendor's bill number can be at most {VendorInvoiceNoMaxLength} characters." });
            var notes = Blank(billDto.Notes);
            if (notes?.Length > NotesMaxLength)
                return BadRequest(new { message = $"Notes can be at most {NotesMaxLength} characters." });

            var vendor = _tenantGuard.EnsureOwned<Vendor>(billDto.VendorId, nameof(billDto.VendorId));
            var lineAccounts = BillAccountsQuery().Select(a => a.Id).ToHashSet();
            var requestedItemIds = billDto.Lines.Where(l => l.ItemId > 0).Select(l => l.ItemId!.Value).Distinct().ToArray();
            var stockAccountsByItem = _db.Items
                .Include(i => i.ItemCategory)
                .Where(i => requestedItemIds.Contains(i.Id)
                    && i.ItemCategory != null
                    && i.ItemCategory.ItemType == Core.Domain.ItemTypes.Purchased
                    && i.InventoryAccountId.HasValue)
                .ToDictionary(i => i.Id, i => i.InventoryAccountId!.Value);
            foreach (var line in billDto.Lines)
            {
                if (line.ItemId > 0)
                    _tenantGuard.EnsureOwned<Core.Domain.Items.Item>(line.ItemId.Value, nameof(line.ItemId));
                if (line.TaxId > 0)
                    _tenantGuard.EnsureOwned<Core.Domain.TaxSystem.Tax>(line.TaxId.Value, nameof(line.TaxId));
                if (line.ItemId.HasValue && stockAccountsByItem.TryGetValue(line.ItemId.Value, out var inventoryAccountId))
                    line.AccountId = inventoryAccountId;
                if (line.AccountId > 0)
                {
                    _tenantGuard.EnsureOwned<Core.Domain.Financials.Account>(line.AccountId.Value, nameof(line.AccountId));
                    if (!lineAccounts.Contains(line.AccountId.Value))
                        return BadRequest(new { message = "Book each line to an expense or asset account, not a cash, receivable or summary account." });
                }
            }
            int? paymentTermId = billDto.PaymentTermId > 0 ? billDto.PaymentTermId : null;
            if (paymentTermId.HasValue)
                _tenantGuard.EnsureOwned<PaymentTerm>(paymentTermId.Value, nameof(billDto.PaymentTermId));

            try
            {
                using var tx = BeginNumberingTransaction();

                PurchaseInvoiceHeader bill;
                if (billDto.Id == 0)
                {
                    bill = new PurchaseInvoiceHeader();
                }
                else
                {
                    _tenantGuard.EnsureOwned<PurchaseInvoiceHeader>(billDto.Id, nameof(billDto.Id));
                    bill = _db.PurchaseInvoiceHeaders.Include(b => b.PurchaseInvoiceLines).Single(b => b.Id == billDto.Id);
                    if (!bill.IsDraft)
                        return BadRequest(new { message = "This bill is approved, so it can't be changed. Void it and enter it again instead." });
                }

                if (vendor.ArchivedAt != null && bill.VendorId != vendor.Id)
                    return BadRequest(new { message = "This vendor is archived. Restore it to enter bills from it." });
                // The same vendor bill entered twice would be paid twice.
                if (vendorInvoiceNo != null)
                {
                    var key = vendorInvoiceNo.ToLower();
                    if (_db.PurchaseInvoiceHeaders.Any(b => b.Id != billDto.Id && b.VendorId == vendor.Id
                            && b.VendorInvoiceNo.ToLower() == key && b.Status != PurchaseInvoiceStatus.Void))
                        return Conflict(new { message = $"Bill {vendorInvoiceNo} from this vendor has already been entered.", code = "duplicate_bill" });
                }

                bill.VendorId = vendor.Id;
                bill.VendorInvoiceNo = vendorInvoiceNo ?? "";
                bill.Date = billDto.Date.Date;
                // No term with a due date means custom terms; with neither, the vendor's term or the workspace default.
                bill.PaymentTermId = paymentTermId
                    ?? (billDto.DueDate.HasValue ? null : vendor.PaymentTermId ?? _adminService.GetPaymentTerms().FirstOrDefault()?.Id);
                bill.DueDate = DueDateForSave(bill.Date, billDto.DueDate, bill.PaymentTermId);
                bill.ReferenceNo = Blank(billDto.ReferenceNo);
                bill.Notes = notes;
                bill.Description = string.IsNullOrEmpty(bill.VendorInvoiceNo) ? null : $"Bill {bill.VendorInvoiceNo}";
                bill.Status = PurchaseInvoiceStatus.Draft;
                bill.ModifiedBy = GetUserNameFromRequestHeader();

                var items = _inventoryService.GetAllItems().ToList();
                var existing = bill.PurchaseInvoiceLines.ToList();
                var kept = new HashSet<int>();
                foreach (var lineDto in billDto.Lines)
                {
                    var line = lineDto.Id > 0 ? existing.FirstOrDefault(l => l.Id == lineDto.Id) : null;
                    if (line == null)
                    {
                        line = new PurchaseInvoiceLine();
                        bill.PurchaseInvoiceLines.Add(line);
                    }
                    else
                    {
                        kept.Add(line.Id);
                    }

                    var item = lineDto.ItemId > 0
                        ? items.First(i => i.Id == lineDto.ItemId)
                        : Api.Service.CatalogDefaults.ResolveFreeTextItem(_inventoryService, _financialService, items, lineDto.Description);
                    line.ItemId = item.Id;
                    line.Description = string.IsNullOrWhiteSpace(lineDto.Description) ? item.Description : lineDto.Description.Trim();
                    line.Quantity = lineDto.Quantity;
                    line.Amount = lineDto.Amount;
                    line.Discount = lineDto.Discount is > 0 ? lineDto.Discount : null;
                    line.AccountId = lineDto.AccountId > 0 ? lineDto.AccountId : null;
                    line.TaxId = lineDto.TaxId > 0 ? lineDto.TaxId : null;
                }
                foreach (var removed in existing.Where(l => !kept.Contains(l.Id)))
                    bill.PurchaseInvoiceLines.Remove(removed);

                _purchasingService.SaveBill(bill);
                if (approving)
                    _purchasingService.ApproveBill(bill.Id);
                tx.Commit();

                return Ok(ToBill(BillsQuery().Single(b => b.Id == bill.Id), _adminService.GetPaymentTerms().ToList()));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost]
        [Route("ApproveBill")]
        [RequirePermission(SystemPermissions.PurchasesBillsPost)]
        public IActionResult ApproveBill(int id)
        {
            _tenantGuard.EnsureOwned<PurchaseInvoiceHeader>(id, nameof(id));
            return BillAction(id, () => _purchasingService.ApproveBill(id), "approve");
        }

        /// <summary>Voids a bill; an approved one has its posting reversed. A bill with payments can't be voided.</summary>
        [HttpPost]
        [Route("VoidBill")]
        [RequirePermission(SystemPermissions.PurchasesBillsPost)]
        public IActionResult VoidBill(int id)
        {
            _tenantGuard.EnsureOwned<PurchaseInvoiceHeader>(id, nameof(id));
            return BillAction(id, () => _purchasingService.VoidBill(id), "void");
        }

        /// <summary>Deletes a draft bill. An approved bill is voided instead, so the ledger keeps its history.</summary>
        [HttpDelete]
        [Route("DeleteBill")]
        [RequirePermission(SystemPermissions.PurchasesBillsCreate)]
        public IActionResult DeleteBill(int id)
        {
            _tenantGuard.EnsureOwned<PurchaseInvoiceHeader>(id, nameof(id));
            var bill = _db.PurchaseInvoiceHeaders.Include(b => b.PurchaseInvoiceLines).Single(b => b.Id == id);
            if (!bill.IsDraft)
                return BadRequest(new { message = "Only a draft bill can be deleted. Void an approved bill instead." });

            // Lines first, then the bill: D1 replays the statements in order and enforces foreign keys.
            using var tx = _db.Database.BeginTransaction();
            _db.PurchaseInvoiceLines.RemoveRange(bill.PurchaseInvoiceLines);
            _db.SaveChanges();
            _db.PurchaseInvoiceHeaders.Remove(bill);
            _db.SaveChanges();
            tx.Commit();
            return NoContent();
        }

        /// <summary>Records a payment on an approved bill, from a cash or bank account.</summary>
        [HttpPost]
        [Route("PayBill")]
        [RequirePermission(SystemPermissions.PurchasesPaymentsCreate)]
        public IActionResult PayBill([FromBody] Dto.Purchasing.PayBill paymentDto)
        {
            _tenantGuard.EnsureOwned<PurchaseInvoiceHeader>(paymentDto.BillId, nameof(paymentDto.BillId));

            // #413 1C: bank → Accounting GL AccountId → payment default → unlinked cash only when no banks exist.
            int accountId;
            try
            {
                accountId = _bankAccounts.ResolvePaymentAccountId(paymentDto.BankAccountId, paymentDto.AccountId);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            return BillAction(paymentDto.BillId, () => _purchasingService.PayBill(paymentDto.BillId, accountId, paymentDto.Amount,
                (paymentDto.Date ?? DateTime.UtcNow).Date,
                Blank(paymentDto.PaymentMethod), Blank(paymentDto.ReferenceNo), Blank(paymentDto.Memo)), "pay");
        }

        /// <summary>The accounts a bill line can be booked to: postable expense and asset accounts, other than cash and receivables.</summary>
        [HttpGet]
        [Route("BillAccounts")] // api/Purchasing/BillAccounts
        [RequirePermission(SystemPermissions.PurchasesBillsView)]
        public IActionResult BillAccounts() =>
            Ok(BillAccountsQuery().OrderBy(a => a.AccountClassId == (int)AccountClasses.Expense ? 0 : 1).ThenBy(a => a.AccountCode)
                .Select(a => ToBillAccount(a)).ToList());

        /// <summary>The cash and bank accounts a bill can be paid from.</summary>
        [HttpGet]
        [Route("PaymentAccounts")] // api/Purchasing/PaymentAccounts
        [RequirePermission(SystemPermissions.PurchasesPaymentsCreate)]
        public IActionResult PaymentAccounts() =>
            Ok(PaymentAccountsQuery().OrderBy(a => a.AccountCode).Select(a => ToBillAccount(a)).ToList());

        /// <summary>Runs a change to one bill in a transaction and returns the bill, or the reason it was refused.</summary>
        private IActionResult BillAction(int id, Action action, string verb)
        {
            try
            {
                using (var tx = BeginNumberingTransaction())
                {
                    action();
                    tx.Commit();
                }
                return Ok(ToBill(BillsQuery().Single(b => b.Id == id), _adminService.GetPaymentTerms().ToList()));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                _logger.LogInformation("Could not {Verb} bill {BillId}: {Reason}", verb, id, ex.Message);
                return BadRequest(new { message = ex.Message });
            }
        }

        // On SQLite, Serializable is BEGIN IMMEDIATE: the write lock is taken up front, so concurrent approvals queue
        // behind each other instead of both reading the same next bill number.
        private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction BeginNumberingTransaction() =>
            RelationalDatabaseFacadeExtensions.BeginTransaction(_db.Database, System.Data.IsolationLevel.Serializable);

        private IQueryable<PurchaseInvoiceHeader> BillsQuery() => _db.PurchaseInvoiceHeaders
            .Include(b => b.Vendor).ThenInclude(v => v!.Party)
            .Include(b => b.Vendor).ThenInclude(v => v!.BillToContact!).ThenInclude(c => c.Party)
            .Include(b => b.PurchaseInvoiceLines).ThenInclude(l => l.Item).ThenInclude(i => i!.ItemCategory)
            .Include(b => b.PurchaseInvoiceLines).ThenInclude(l => l.Account)
            .Include(b => b.VendorPayments).ThenInclude(p => p.Account);

        private IQueryable<Core.Domain.Financials.Account> BillAccountsQuery()
        {
            var receivables = _db.Customers.Where(c => c.AccountsReceivableAccountId != null).Select(c => c.AccountsReceivableAccountId!.Value);
            return _db.Accounts.Where(a => a.ArchivedAt == null
                && (a.AccountClassId == (int)AccountClasses.Expense || a.AccountClassId == (int)AccountClasses.Assets)
                && !a.ChildAccounts.Any()
                && !a.IsCash && !(a.ParentAccount != null && a.ParentAccount.IsCash)
                && a.AccountCode != "10120" && !receivables.Contains(a.Id));
        }

        // Summary accounts such as 10110 can't take postings, so only postable cash accounts qualify.
        private IQueryable<Core.Domain.Financials.Account> PaymentAccountsQuery() =>
            _db.Accounts.Where(a => a.ArchivedAt == null && !a.ChildAccounts.Any() && (a.IsCash || (a.ParentAccount != null && a.ParentAccount.IsCash)));

        private static Dto.Purchasing.BillAccount ToBillAccount(Core.Domain.Financials.Account account) => new()
        {
            Id = account.Id,
            Code = account.AccountCode,
            Name = account.AccountName,
            Group = account.AccountClassId == (int)AccountClasses.Expense ? "Expense" : "Asset",
            IsInventoryAccount = account.AccountCode == "10800",
        };

        private static Dto.Purchasing.Bill ToBill(PurchaseInvoiceHeader bill, List<PaymentTerm> terms)
        {
            var dueDate = bill.ResolveDueDate(terms.FirstOrDefault(t => t.Id == bill.PaymentTermId));
            // Approved bills show the vendor as it was then; drafts, and bills approved before that was recorded, as it is now.
            var from = Core.Domain.Sales.InvoiceBillTo.FromJson(bill.BillFromJson)
                ?? Core.Domain.Sales.InvoiceBillTo.FromParty(bill.Vendor?.Party, bill.Vendor?.BillToContact);
            return new Dto.Purchasing.Bill
            {
                Id = bill.Id,
                No = bill.No,
                VendorId = bill.VendorId ?? 0,
                VendorName = from.Name,
                VendorEmail = from.Email,
                VendorPhone = from.Phone,
                VendorAddress = from.AddressText,
                VendorInvoiceNo = string.IsNullOrEmpty(bill.VendorInvoiceNo) ? null : bill.VendorInvoiceNo,
                Date = bill.Date,
                DueDate = dueDate,
                PaymentTermId = bill.PaymentTermId,
                ReferenceNo = bill.ReferenceNo,
                Notes = bill.Notes,
                Lines = bill.PurchaseInvoiceLines.OrderBy(l => l.Id).Select(l => new Dto.Purchasing.BillLine
                {
                    Id = l.Id,
                    ItemId = l.ItemId,
                    Description = l.DisplayDescription,
                    AccountId = l.AccountId,
                    AccountName = l.Account == null ? null : $"{l.Account.AccountCode} · {l.Account.AccountName}",
                    IsStockItem = l.Item?.ItemCategory?.ItemType == Core.Domain.ItemTypes.Purchased
                        && l.Item.InventoryAccountId.HasValue,
                    Quantity = l.Quantity,
                    Amount = l.Amount,
                    Discount = l.Discount,
                    TaxId = l.TaxId,
                    TaxAmount = l.TaxAmount,
                    Subtotal = l.Subtotal,
                }).ToList(),
                Subtotal = bill.Subtotal(),
                TotalTax = bill.TotalTax(),
                Total = bill.Total(),
                AmountPaid = bill.AmountPaid(),
                BalanceDue = bill.IsVoid ? 0 : bill.BalanceDue(),
                Status = bill.DisplayStatus(dueDate, DateTime.UtcNow),
                Approved = bill.GeneralLedgerHeaderId.HasValue,
                Payments = bill.VendorPayments.OrderByDescending(p => p.Date).ThenByDescending(p => p.Id).Select(p => new Dto.Purchasing.BillPayment
                {
                    Id = p.Id,
                    No = p.No,
                    Date = p.Date,
                    Amount = p.Amount,
                    AccountId = p.AccountId,
                    AccountName = p.Account == null ? null : $"{p.Account.AccountCode} · {p.Account.AccountName}",
                    PaymentMethod = p.PaymentMethod,
                    ReferenceNo = p.ReferenceNo,
                    Memo = p.Memo,
                }).ToList(),
            };
        }

        /// <summary>
        /// The due date sent with the bill, or else the term's business days after the bill date, as invoices do. A due
        /// date before the bill date is refused.
        /// </summary>
        private DateTime DueDateForSave(DateTime billDate, DateTime? requested, int? paymentTermId)
        {
            if (requested.HasValue)
            {
                if (requested.Value.Date < billDate.Date)
                    throw new ArgumentException("The due date can't be before the bill date.");
                return requested.Value.Date;
            }
            var term = paymentTermId.HasValue ? _adminService.GetPaymentTerms().FirstOrDefault(t => t.Id == paymentTermId) : null;
            return BusinessDays.Add(billDate, term?.DueAfterDays ?? 0);
        }

        private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static bool ValidEmail(string value) =>
            System.Net.Mail.MailAddress.TryCreate(value, out var address) && address.Address == value;
    }
}
