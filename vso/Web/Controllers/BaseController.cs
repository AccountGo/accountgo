using Core;
using System.IO;
using System.Web.Mvc;
using Web.Models;

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

        //protected abstract string GetPageTitle();
    }
}