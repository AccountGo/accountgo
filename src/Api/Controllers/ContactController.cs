using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Services.Sales;
using Services.Purchasing;

// For more information on enabling MVC for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class ContactController : BaseController
    {

        private readonly ISalesService _salesService;
        private readonly IPurchasingService _purchasingService;
        public ContactController(ISalesService salesService, IPurchasingService purchasingService)
        {
            _salesService = salesService;
            _purchasingService = purchasingService;
        }

        //[HttpGet]
        //[Route("[action]")]
        //public IActionResult Contacts(int customerId)
        //{
        //    var customer = _salesService.GetCustomerById(customerId);

        //    //var customerContact = customer.CustomerContact;

        //    var customerContact = customer.CustomerContact;

        //    //var contacts = _salesService.GetContacts();//.Where(a => customerContact.Select(x => x.ContactId).Contains(a.Id));
       



        //    //ICollection<ContactDto> contactsDto = new HashSet<ContactDto>();
        //    var contactsDto = new List<Dto.Common.Contact>();

        //    foreach (var contact in customerContact.Select(a => a.Contact).ToList())
        //        contactsDto.Add(new Dto.Common.Contact() { Id = contact.Id, FirstName = contact.FirstName, LastName = contact.LastName, CustomerId = customerId });

        //    return Ok(contactsDto.AsEnumerable());
        //}


        [HttpGet]
        [Route("[action]")]
        public IActionResult Contacts(int partyId = 0, int partyType = 0)
        {
            var contactsDto = new List<Dto.Common.Contact>();
            if (partyType == 1) // for customer
            {
                var customer = _salesService.GetCustomerById(partyId);
                var customerContact = customer.CustomerContact;
                foreach (var contact in customerContact.Select(a => a.Contact).ToList())
                    contactsDto.Add(new Dto.Common.Contact() { Id = contact.Id, FirstName = contact.FirstName, LastName = contact.LastName, HoldingPartyId = partyId, HoldingPartyType = 1 });
            }
            else
            {
                var vendor = _purchasingService.GetVendorById(partyId);
                var vendorContact = vendor.VendorContact;       
                foreach (var contact in vendorContact.Select(a => a.Contact).ToList())
                    contactsDto.Add(new Dto.Common.Contact() { Id = contact.Id, FirstName = contact.FirstName, LastName = contact.LastName, HoldingPartyId = partyId, HoldingPartyType = 2 });

                return Ok(contactsDto.AsEnumerable());

            }
            return Ok(contactsDto.AsEnumerable());
        }
        //[HttpGet]
        //[Route("[action]")]
        //public IActionResult Contact(int id, int customerId)
        //{
        //    var contact = _salesService.GetContacyById(id);

        //    var contactDto = new Dto.Common.Contact();
        //    var partyDto = new Dto.Common.Party();

        //    partyDto.Email = contact.Party.Email;
        //    partyDto.Fax = contact.Party.Fax;
        //    partyDto.Phone = contact.Party.Phone;
        //    partyDto.Website = contact.Party.Website;
        //    partyDto.Id = contact.Party.Id;
        //    contactDto.CustomerId = customerId;
        //    contactDto.FirstName = contact.FirstName;
        //    contactDto.LastName = contact.LastName;
        //    contactDto.Id = contact.Id;
        //    contactDto.Party = partyDto;

        //    //contactDto.Party = contact.Party;
        //    return new ObjectResult(contactDto);

        //}

        [HttpGet]
        [Route("[action]")]
        public IActionResult Contact(int id, int partyId, int partyType)
        {
            Core.Domain.Contact contact = _salesService.GetContacyById(id);
 
            var contactDto = new Dto.Common.Contact();
            var partyDto = new Dto.Common.Party();

            partyDto.Email = contact.Party.Email;
            partyDto.Fax = contact.Party.Fax;
            partyDto.Phone = contact.Party.Phone;
            partyDto.Website = contact.Party.Website;
            partyDto.Id = contact.Party.Id;

            contactDto.FirstName = contact.FirstName;
            contactDto.LastName = contact.LastName;
            contactDto.Id = contact.Id;
            contactDto.Party = partyDto;

            contactDto.HoldingPartyType = partyType;
            contactDto.HoldingPartyId = partyId;


            //contactDto.Party = contact.Party;
            return new ObjectResult(contactDto);

        }


        [HttpPost]
        [Route("[action]")]
        public IActionResult SaveContact([FromBody]Dto.Common.Contact model)
        {
            string[] errors = null;
            try
            {
                Core.Domain.Contact contact = null;


                if (model.Id == 0)
                {
                    contact = new Core.Domain.Contact();
                    contact.Party = new Core.Domain.Party()
                    {
                        PartyType = Core.Domain.PartyTypes.Customer,
                    };
                }
                else
                {
                    contact = _salesService.GetContacyById(model.Id);
                }
                //Contact
                contact.Id = model.Id;
                contact.ContactType = (Core.Domain.ContactTypes)model.HoldingPartyType; // customer or vendor
                contact.FirstName = model.FirstName;
                contact.MiddleName = model.MiddleName;
                contact.LastName = model.LastName;
                //Party
 
                contact.Party.Website = model.Party.Website;
                contact.Party.Email = model.Party.Email;
                contact.Party.Phone = model.Party.Phone;
                 
                if (contact.Id > 0)
                {
                    _salesService.SaveContact(contact);
                    //_salesService.UpdateContact(contact);
                }
                else
                {

                    if (model.HoldingPartyType == 1)
                    {

                        var customer = _salesService.GetCustomerById(model.HoldingPartyId);

                        if (customer.PrimaryContact == null)
                        {
                            customer.PrimaryContact = contact;
                        }

                        var customerContact = new Core.Domain.CustomerContact();
                        customerContact.Contact = contact;
                        customerContact.Contact.Party = contact.Party;
                        customerContact.CustomerId = customer.Id;
                        customer.CustomerContact.Add(customerContact);
                        _salesService.UpdateCustomer(customer);

                    }
                    else
                    {
                        var vendor = _purchasingService.GetVendorById(model.HoldingPartyId);

                        if (vendor.PrimaryContact == null)
                        {
                            vendor.PrimaryContact = contact;
                        }

                        var vendorContact = new Core.Domain.VendorContact();
                        vendorContact.Contact = contact;
                        vendorContact.Contact.Party = contact.Party;
                        vendorContact.VendorId = vendor.Id;
                        vendor.VendorContact.Add(vendorContact);
                        _purchasingService.UpdateVendor(vendor);

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


    }
}
