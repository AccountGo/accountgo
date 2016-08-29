using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Services.Sales;

// For more information on enabling MVC for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class ContactController : BaseController
    {

        private readonly ISalesService _salesService;

        public ContactController(ISalesService salesService)
        {
            _salesService = salesService;
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Contacts()
        {
            var contacts = _salesService.GetContacts();

            //ICollection<ContactDto> contactsDto = new HashSet<ContactDto>();
            var contactsDto = new List<Dto.Common.Contact>();

            foreach (var contact in contacts)
                contactsDto.Add(new Dto.Common.Contact() { Id = contact.Id, FirstName = contact.FirstName, LastName = contact.LastName });

            return Ok(contactsDto.AsEnumerable());
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Contact(int id)
        {
            var contact = _salesService.GetContacyById(id);

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

                var contact = new Core.Domain.Contact();
                var party = new Core.Domain.Party();

                if (contact.Id == 0)
                {
                    contact.Party = new Core.Domain.Party()
                    {
                        PartyType = Core.Domain.PartyTypes.Customer,
                    };
                }
                //Contact
                contact.Id = model.Id;
                contact.ContactType = Core.Domain.ContactTypes.Customer;
                contact.FirstName = model.FirstName;
                contact.MiddleName = model.MiddleName;
                contact.LastName = model.LastName;
                //Party
 
                contact.Party.Website = model.Party.Website;
                contact.Party.Email = model.Party.Email;
                contact.Party.Phone = model.Party.Phone;

                _salesService.SaveContact(contact);


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
