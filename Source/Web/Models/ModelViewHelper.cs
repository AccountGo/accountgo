//-----------------------------------------------------------------------
// <copyright file="ModelViewHelper.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Purchases;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web;
using System.Web.Mvc;
using Web.ModelsApi.Inventory;
using Web.ModelsApi.Purchasing;
using Web.ModelsApi.Sales;
using System.Linq;
using Web.ModelsApi;
using Web.ModelsApi.Financial;

namespace Web.Models
{
    public sealed class ModelViewHelper
    {
        public static string FullyQualifiedApplicationPath
        {
            get
            {
                //Return variable declaration
                var appPath = string.Empty;

                //Getting the current context of HTTP request
                var context = HttpContext.Current;

                if (context.Request.IsLocal)
                {
                    //Checking the current context content
                    if (context != null)
                    {
                        //Formatting the fully qualified website url/name
                        appPath = string.Format("{0}://{1}{2}{3}",
                                                context.Request.Url.Scheme,
                                                context.Request.Url.Host,
                                                context.Request.Url.Port == 80
                                                    ? string.Empty
                                                    : ":" + context.Request.Url.Port,
                                                context.Request.ApplicationPath);
                    }
                }
                else
                {
                    appPath = System.Configuration.ConfigurationManager.AppSettings["BaseUrl"];
                }

                if (!appPath.EndsWith("/"))
                    appPath += "/";

                return appPath;
            }
        }

        public static ICollection<SelectListItem> Accounts()
        {
            var selections = new HashSet<SelectListItem>();
            selections.Add(new SelectListItem() { Text = string.Empty, Value = "-1", Selected = true });

            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(FullyQualifiedApplicationPath);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = client.GetAsync("api/common/accounts").Result;

                if (response.IsSuccessStatusCode)
                {
                    var accounts = response.Content.ReadAsAsync<IEnumerable<Account>>().Result;
                    foreach (var account in accounts)
                        selections.Add(new SelectListItem() { Text = account.AccountName, Value = account.Id.ToString() });
                }
            }

            return selections;
        }

        public static ICollection<SelectListItem> Items()
        {
            var selections = new HashSet<SelectListItem>();
            selections.Add(new SelectListItem() { Text = string.Empty, Value = "-1", Selected = true });

            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(FullyQualifiedApplicationPath);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = client.GetAsync("api/common/items").Result;

                if (response.IsSuccessStatusCode)
                {
                    var items = response.Content.ReadAsAsync<IEnumerable<ItemDto>>().Result;
                    foreach (var item in items)
                        selections.Add(new SelectListItem() { Text = item.Description, Value = item.No });
                }
            }

            return selections;
        }

        public static ICollection<SelectListItem> Measurements()
        {
            var selections = new HashSet<SelectListItem>();
            selections.Add(new SelectListItem() { Text = string.Empty, Value = "-1", Selected = true });

            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(FullyQualifiedApplicationPath);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = client.GetAsync("api/common/measurements").Result;

                if (response.IsSuccessStatusCode)
                {
                    var measurements = response.Content.ReadAsAsync<IEnumerable<Measurement>>().Result;
                    foreach (var measurement in measurements)
                        selections.Add(new SelectListItem() { Text = measurement.Code, Value = measurement.Id.ToString() });
                }
            }

            return selections;
        }

        public static ICollection<SelectListItem> ItemCategories()
        {
            var selections = new HashSet<SelectListItem>();

            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(FullyQualifiedApplicationPath);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = client.GetAsync("api/common/itemcategories").Result;

                if (response.IsSuccessStatusCode)
                {
                    var itemcategories = response.Content.ReadAsAsync<IEnumerable<ItemCategory>>().Result;
                    foreach (var itemCategory in itemcategories)
                        selections.Add(new SelectListItem() { Text = itemCategory.Name, Value = itemCategory.Id.ToString() });
                }
            }

            return selections;
        }

        public static ICollection<SelectListItem> Vendors()
        {
            var selections = new HashSet<SelectListItem>();

            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(FullyQualifiedApplicationPath);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = client.GetAsync("api/common/vendors").Result;

                if (response.IsSuccessStatusCode)
                {
                    var vendors = response.Content.ReadAsAsync<IEnumerable<VendorDto>>().Result;
                    foreach (var vendor in vendors)
                        selections.Add(new SelectListItem() { Text = vendor.Name, Value = vendor.Id.ToString() });
                }
            }

            return selections;
        }

        public static ICollection<SelectListItem> Customers()
        {
            var selections = new HashSet<SelectListItem>();
            selections.Add(new SelectListItem() { Text = string.Empty, Value = "-1", Selected = true });

            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(FullyQualifiedApplicationPath);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = client.GetAsync("api/common/customers").Result;

                if (response.IsSuccessStatusCode)
                {
                    var customers = response.Content.ReadAsAsync<IEnumerable<CustomerDto>>().Result;
                    foreach (var customer in customers)
                        selections.Add(new SelectListItem() { Text = customer.Name, Value = customer.Id.ToString() });
                }
            }

            return selections;
        }

        public static ICollection<SelectListItem> Contacts()
        {
            var selections = new HashSet<SelectListItem>();
            selections.Add(new SelectListItem() { Text = string.Empty, Value = "-1", Selected = true });

            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(FullyQualifiedApplicationPath);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = client.GetAsync("api/common/contacts").Result;

                if (response.IsSuccessStatusCode)
                {
                    var contacts = response.Content.ReadAsAsync<IEnumerable<ContactDto>>().Result;
                    foreach (var contact in contacts)
                        selections.Add(new SelectListItem() { Text = contact.FullName, Value = contact.Id.ToString() });
                }
            }

            return selections;
        }
        
        public static ICollection<SelectListItem> Taxes()
        {
            var selections = new HashSet<SelectListItem>();
            selections.Add(new SelectListItem() { Text = string.Empty, Value = "-1", Selected = true });

            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(FullyQualifiedApplicationPath);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = client.GetAsync("api/common/taxes").Result;

                if (response.IsSuccessStatusCode)
                {
                    var taxes = response.Content.ReadAsAsync<IEnumerable<Tax>>().Result;
                    foreach (var tax in taxes)
                        selections.Add(new SelectListItem() { Text = tax.TaxCode, Value = tax.Id.ToString() });
                }
            }

            return selections;
        }

        public static ICollection<SelectListItem> ItemTaxGroups()
        {
            var selections = new HashSet<SelectListItem>();
            selections.Add(new SelectListItem() { Text = string.Empty, Value = "-1", Selected = true });
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(FullyQualifiedApplicationPath);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = client.GetAsync("api/common/itemtaxgroups").Result;

                if (response.IsSuccessStatusCode)
                {
                    var itemtaxgroups = response.Content.ReadAsAsync<IEnumerable<ItemTaxGroup>>().Result;
                    foreach (var tax in itemtaxgroups)
                        selections.Add(new SelectListItem() { Text = tax.Name, Value = tax.Id.ToString() });
                }
            }

            return selections;
        }

        public static ICollection<SelectListItem> TaxGroups()
        {
            var selections = new HashSet<SelectListItem>();
            selections.Add(new SelectListItem() { Text = string.Empty, Value = "-1", Selected = true });
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(FullyQualifiedApplicationPath);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = client.GetAsync("api/common/taxgroups").Result;

                if (response.IsSuccessStatusCode)
                {
                    var taxgroups = response.Content.ReadAsAsync<IEnumerable<TaxGroup>>().Result;
                    foreach (var tax in taxgroups)
                        selections.Add(new SelectListItem() { Text = tax.Description, Value = tax.Id.ToString() });
                }
            }

            return selections;
        }

        public static ICollection<SelectListItem> TransactionTypes()
        {
            var selections = new HashSet<SelectListItem>();
            return selections;
        }

        public static ICollection<SelectListItem> AccountClasses()
        {
            var EntityState = new SelectList(Enum.GetValues(typeof(AccountClasses)).Cast<AccountClasses>().Select(v => new SelectListItem
            {
                Text = v.ToString(),
                Value = ((int)v).ToString()
            }).ToList(), "Value", "Text");
            return EntityState.ToList();
        }

        public static ICollection<SelectListItem> PaymentTerms()
        {
            var selections = new HashSet<SelectListItem>();
            selections.Add(new SelectListItem() { Text = string.Empty, Value = "-1", Selected = true });

            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(FullyQualifiedApplicationPath);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = client.GetAsync("api/common/paymentterms").Result;

                if (response.IsSuccessStatusCode)
                {
                    var paymentterms = response.Content.ReadAsAsync<IEnumerable<PaymentTerm>>().Result;
                    foreach (var paymentterm in paymentterms)
                        selections.Add(new SelectListItem() { Text = paymentterm.Description, Value = paymentterm.Id.ToString() });
                }
            }

            return selections;
        }

        public static ICollection<SelectListItem> Banks()
        {
            var selections = new HashSet<SelectListItem>();
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(FullyQualifiedApplicationPath);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = client.GetAsync("api/common/banks").Result;

                if (response.IsSuccessStatusCode)
                {
                    var banks = response.Content.ReadAsAsync<IEnumerable<BankDto>>().Result;

                    foreach (var bank in banks)
                        selections.Add(new SelectListItem() { Text = bank.BankName, Value = bank.AccountId.ToString() });
                }
            }

            return selections;
        }
    }
}
