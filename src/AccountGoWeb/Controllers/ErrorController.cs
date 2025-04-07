using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Diagnostics;

namespace AccountGoWeb.Controllers
{
    // Controller responsible for handling various error scenarios
    public class ErrorController : GoodController
    {
        private readonly ILogger<ErrorController> _logger;

        public ErrorController(IConfiguration config, ILogger<ErrorController> logger)
        {
            _configuration = config;
            _logger = logger;
        }

        // Handles HTTP status code errors like 404, 500, etc.
        // Maps to route "/Error/{statusCode}" as configured in Program.cs
        [Route("Error/{statusCode}")]
        public IActionResult HttpStatusCodeHandler(int statusCode)
        {
            var statusCodeResult = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();

            ViewBag.PageContentHeader = $"Error {statusCode}";

            switch (statusCode)
            {
                case 404:
                    ViewBag.ErrorMessage = "Sorry, the page you requested could not be found";
                    _logger.LogWarning($"404 error occurred. Path = {statusCodeResult?.OriginalPath}");
                    break;
                case 500:
                    ViewBag.ErrorMessage = "An internal server error occurred";
                    _logger.LogError($"500 error occurred. Path = {statusCodeResult?.OriginalPath}");
                    break;
                default:
                    // Generic message for other status codes
                    ViewBag.ErrorMessage = "An error occurred";
                    break;
            }

            return View("Error");
        }

        // Handles uncaught exceptions throughout the application
        // Maps to route "/Error" as configured in Program.cs via UseExceptionHandler
        [Route("Error")]
        public IActionResult Error()
        {
            var exceptionDetails = HttpContext.Features.Get<IExceptionHandlerPathFeature>();

            ViewBag.PageContentHeader = "Error";
            ViewBag.ErrorMessage = "An unexpected error occurred";

            _logger.LogError($"Exception: {exceptionDetails?.Error?.Message} occurred at {exceptionDetails?.Path}");

            return View();
        }
    }
}