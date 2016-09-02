using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using Dto.Common;
// For more information on enabling MVC for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace AccountGoWeb.Controllers
{
    public class ContactController : BaseController
    {

        public ContactController(IConfiguration config)
        {
            _baseConfig = config;
            Models.SelectListItemHelper._config = config;

        }

        // GET: /<controller>/
        public IActionResult Index()
        {
            return View();
        }
 
        public async System.Threading.Tasks.Task<IActionResult> Contacts(int partyId = 0, int partyType = 0)
        {
            ViewBag.PageContentHeader = "Contacts";

            //var contacts = GetAsync<IEnumerable<Dto.Common.Contact>>("contact/contacts");

            //return View(model: contacts);
            using (var client = new HttpClient())
            {
                var baseUri = _baseConfig["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear(); 
                var response = await client.GetAsync(baseUri + "contact/contacts?partyId=" + partyId + "&partyType=" + partyType);
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return View(model: responseJson);
                }
            }
            return View();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id">contact id</param>
        /// <param name="partyId">Id of customer or the vendor</param>
        /// <param name="partyType">type of party holding the contact (e.g customer or vendor type)</param>
        /// <returns></returns>
        public IActionResult Contact(int id = 0, int partyId = 0, int partyType = 0)
        {
            Contact contact = null;

            if (id == 0) // creating new contact
            {
                ViewBag.PageContentHeader = "New Contact";
                contact = new Contact();
                contact.HoldingPartyType = partyType;
                contact.HoldingPartyId = partyId;
                // contact.Party is for contact itself. 
                // contact.HoldingPartyId is the id of the customer/or vendor. this is the partyId parameter
                // contact.HoldingPartyType is the type of the holding party, 1 = customer, 2 = vendor
            }
            else // editing existing contact
            {
                ViewBag.PageContentHeader = "Contact Card";
                contact = GetAsync<Contact>("contact/contact?id=" + id + "&partyId=" + partyId + "&partyType=" + partyType).Result;
            }

            return View(contact);
        }

        //public IActionResult Contact(int id, int customerId, int vendorId)
        //{
        //     Contact contact = null;

        //    if (id > 0)
        //    {
        //        contact = GetAsync<Contact>("contact/contact?id=" + id + "&customerId=" + customerId).Result;
        //    }
        //    else
        //    {
        //        contact = new Contact();
        //        contact.CustomerId = customerId;
        //        contact.VendorId = vendorId;
        //    }


        //    return View(contact);
        //}

        public IActionResult SaveContact(Contact contactModel)
        {
            if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(contactModel);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                var response = PostAsync("contact/savecontact", content);
                return RedirectToAction("Contacts", new { partyId = contactModel.HoldingPartyId, partyType = contactModel.HoldingPartyType });
 
            }
            return View("Contact", contactModel);
        }
    }
}
