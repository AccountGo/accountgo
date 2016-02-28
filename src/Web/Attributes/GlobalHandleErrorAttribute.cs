using NLog;
using System.Web.Mvc;

namespace Web.Attributes
{
    public class GlobalHandleErrorAttribute : HandleErrorAttribute
    {
        private static readonly Logger Nlog = LogManager.GetCurrentClassLogger();
        public override void OnException(ExceptionContext filterContext)
        {
            Nlog.Log(LogLevel.Error, filterContext.Exception);

            base.OnException(filterContext);
        }
    }
}