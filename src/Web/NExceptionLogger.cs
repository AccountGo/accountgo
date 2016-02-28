using NLog;
using System.Web.Http.ExceptionHandling;

namespace Web
{
    public class NLogExceptionLogger : ExceptionLogger
    {
        private static readonly Logger Nlog = LogManager.GetCurrentClassLogger();
        public override void Log(ExceptionLoggerContext context)
        {
            Nlog.Log(LogLevel.Error, context.Exception);
        }
    }
}