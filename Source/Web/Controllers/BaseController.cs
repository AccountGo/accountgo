//-----------------------------------------------------------------------
// <copyright file="BaseController.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Web.Mvc;
using System.Web.Mvc.Filters;

namespace Web.Controllers
{
    public abstract class BaseController : Controller, IController
    {
        protected override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            //ViewData["Title"] = GetPageTitle();
        }

        protected override void OnException(ExceptionContext filterContext)
        {
            //filterContext.ExceptionHandled = true;
            //this.View("Error", new ErrorMessageViewModel(filterContext.Exception.Message)).ExecuteResult(this.ControllerContext);
        }

        public virtual void SetMessage(string msgId)
        {
            TempData["Message"] = MessageSource.GetMessage(msgId);
        }

        protected string RenderPartialViewToString()
        {
            return RenderPartialViewToString(null, null);
        }

        protected string RenderPartialViewToString(string viewName)
        {
            return RenderPartialViewToString(viewName, null);
        }

        protected string RenderPartialViewToString(object model)
        {
            return RenderPartialViewToString(null, model);
        }

        protected string RenderPartialViewToString(string viewName, object model)
        {            
            if (string.IsNullOrEmpty(viewName))
                viewName = ControllerContext.RouteData.GetRequiredString("action");

            ViewData.Model = model;

            using (StringWriter sw = new StringWriter())
            {
                ViewEngineResult viewResult = ViewEngines.Engines.FindPartialView(ControllerContext, viewName);
                ViewContext viewContext = new ViewContext(ControllerContext, viewResult.View, ViewData, TempData, sw);
                viewResult.View.Render(viewContext, sw);

                return sw.GetStringBuilder().ToString();
            }
        }

        protected override void OnAuthentication(AuthenticationContext filterContext)
        {
            if (filterContext.HttpContext.User.Identity.IsAuthenticated)
            {
                IEnumerable<string> permissions = null;

                using (var client = new HttpClient())
                {
                    client.BaseAddress = new Uri(ConfigurationManager.AppSettings["BaseUrl"]);
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    HttpResponseMessage response = client.GetAsync("api/security/GetPermissionsForUser?username=" + User.Identity.Name).Result;

                    if (response.IsSuccessStatusCode)
                    {
                        permissions = response.Content.ReadAsAsync<IEnumerable<string>>().Result;
                    }
                }

                var id = ClaimsPrincipal.Current.Identities.First();
                if (permissions != null)
                {
                    foreach (var permission in permissions)
                    {
                        id.AddClaim(new Claim(ClaimTypes.Role, permission));
                    }
                }
            }
        }
    }
}
