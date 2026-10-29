#nullable enable
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Services.Banking;

namespace Api.Filters
{
    /// <summary>
    /// Carries <c>?confirmReconciledPeriod=true</c> into the request's <see cref="IReconciledPeriodGuard"/>, so any
    /// endpoint that posts to a bank account can be confirmed the same way (3E). Only waives the warning; it grants
    /// no access.
    /// </summary>
    public sealed class ReconciledPeriodConfirmationFilter : IActionFilter
    {
        public const string QueryParameterName = "confirmReconciledPeriod";

        public void OnActionExecuting(ActionExecutingContext context)
        {
            var raw = context.HttpContext.Request.Query[QueryParameterName].ToString();
            if (!bool.TryParse(raw, out var confirmed) || !confirmed)
                return;

            context.HttpContext.RequestServices.GetService<IReconciledPeriodGuard>()?.Confirm();
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }
    }
}
