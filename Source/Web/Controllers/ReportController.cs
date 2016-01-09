//-----------------------------------------------------------------------
// <copyright file="ReportController.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web;
using System.Web.Http;

namespace Web.Controllers
{
    public class ReportController : ControllersApi.BaseApiController
    {
        public HttpResponseMessage Get()
        {
            NameValueCollection nvc = HttpUtility.ParseQueryString(Request.RequestUri.Query);
            string Report = nvc["Report"].ToString();
            Int64 Id = Convert.ToInt64(nvc["Id"]);
            var response = new HttpResponseMessage(HttpStatusCode.OK);

            switch (Report)
            {
                case "PurchaseOrder":
                    //response.Content = new StreamContent(this.PurchaseOrder(Id));
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                    break;
                case "PurchaseInvoice":
                    //response.Content = new StreamContent(this.PurchaseInvoice(Id));
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                    break;
                case "Disbursement":
                    //response.Content = new StreamContent(this.Disbursement(Id));
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                    break;
                case "SalesOrder":
                    //response.Content = new StreamContent(this.SalesOrder(Id));
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                    break;
                case "SalesInvoice":
                    //response.Content = new StreamContent(this.SalesInvoice(Id));
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                    break;
                case "Collection":
                    //response.Content = new StreamContent(this.Collection(Id));
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                    break;
                case "JournalVoucher":
                    //response.Content = new StreamContent(this.JournalVoucher(Id));
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                    break;
                case "StockIn":
                    //response.Content = new StreamContent(this.StockIn(Id));
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                    break;
                case "StockOut":
                    //response.Content = new StreamContent(this.StockOut(Id));
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                    break;
                case "StockTransfer":
                    //response.Content = new StreamContent(this.StockTransfer(Id));
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                    break;
            }

            return response;
        }
        //// GET api/<controller>
        //public IEnumerable<string> Get()
        //{
        //    return new string[] { "value1", "value2" };
        //}

        //// GET api/<controller>/5
        //public string Get(int id)
        //{
        //    return "value";
        //}

        //// POST api/<controller>
        //public void Post([FromBody]string value)
        //{
        //}

        //// PUT api/<controller>/5
        //public void Put(int id, [FromBody]string value)
        //{
        //}

        //// DELETE api/<controller>/5
        //public void Delete(int id)
        //{
        //}
    }
}
