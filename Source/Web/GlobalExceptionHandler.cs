using Logging;
using System;
using System.Net;
using System.Net.Http;
using System.Web.Http.ExceptionHandling;
using System.Web.Http.Results;

namespace Web
{
    public class GlobalExceptionHandler : ExceptionHandler
    {
        public override void Handle(ExceptionHandlerContext context)
        {
            var errorInformation = new ErrorInformation()
            {
                Message = "An unhandled exception occurred; check the log for more information.",
            };

            context.Result = new ResponseMessageResult(context.Request.CreateResponse(HttpStatusCode.BadRequest, errorInformation));
        }
    }

    public class ErrorInformation
    {
        public ErrorInformation()
        {
            ErrorDate = DateTime.UtcNow;
            Level = LogLevel.Fatal.ToString();
        }

        public string Message { get; set; }
        public DateTime ErrorDate { get; set; }
        public string Level { get; set; }
    }
}