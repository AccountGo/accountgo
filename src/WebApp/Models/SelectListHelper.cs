using Microsoft.AspNet.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using WebApp.Models.Sales;

namespace WebApp.Models
{
    public class SelectListHelper
    {
        public static ICollection<SelectListItem> Customers()
        {
            var selections = new HashSet<SelectListItem>();
            selections.Add(new SelectListItem() { Text = string.Empty, Value = "-1", Selected = true });

            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri("");
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = client.GetAsync("api/sales/customers").Result;

                if (response.IsSuccessStatusCode)
                {
                    //var customers = response.Content.ReadAsAsync<IEnumerable<Customer>>().Result;
                    //foreach (var customer in customers)
                    //    selections.Add(new SelectListItem() { Text = customer.Name, Value = customer.Id.ToString() });
                }
            }

            return selections;
        }
    }
}
