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
 
        public async System.Threading.Tasks.Task<IActionResult> Contacts(int id)
        {
            ViewBag.PageContentHeader = "Contacts";

            //var contacts = GetAsync<IEnumerable<Dto.Common.Contact>>("contact/contacts");

            //return View(model: contacts);
            using (var client = new HttpClient())
            {
                var baseUri = _baseConfig["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "contact/contacts?customerId=" + id);
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return View(model: responseJson);
                }
            }
            return View();
        }

        public IActionResult Contact(int id, int customerId, int vendorId)
        {
             Contact contact = null;

            if (id > 0)
            {
                contact = GetAsync<Contact>("contact/contact?id=" + id).Result;
            }
            else
            {
                contact = new Contact();
                contact.CustomerId = customerId;
                contact.VendorId = vendorId;
            }


            return View(contact);
        }

        public IActionResult SaveContact(Contact contactModel)
        {
            if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(contactModel);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                var response = PostAsync("contact/savecontact", content);

                return RedirectToAction("Contacts/" + contactModel.CustomerId);
            }
            return View("Contact", contactModel);
        }
    }
}
