// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Api.PublicIds
{
    /// <summary>
    /// Runs before <c>ModelStateInvalidFilter</c> (order -2000) and before any action code,
    /// mapping public ID binding/deserialization failures to consistent 400 (<c>invalid_id</c>)
    /// and 404 (<c>unknown_id</c>) JSON responses with byte-identical 404 bodies for missing vs foreign IDs.
    /// </summary>
    public sealed class PublicIdErrorFilter : IActionFilter, IExceptionFilter, IOrderedFilter
    {
        public int Order => -3000;

        public void OnException(ExceptionContext context)
        {
            if (context.Exception is PublicIdSerializationException pubEx)
            {
                context.Result = pubEx.Code == PublicIdErrorCodes.InvalidId
                    ? new BadRequestObjectResult(new { message = pubEx.Message, code = PublicIdErrorCodes.InvalidId })
                    : new NotFoundObjectResult(new { message = PublicIdErrorCodes.NotFoundMessage, code = PublicIdErrorCodes.UnknownId });
                context.ExceptionHandled = true;
            }
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            var errors = PublicIdErrorRecorder.GetErrors(context.HttpContext);
            if (errors.Count == 0)
            {
                // Fallback: inspect ModelState exceptions in case Newtonsoft captured PublicIdSerializationException
                foreach (var entry in context.ModelState.Values)
                {
                    foreach (var err in entry.Errors)
                    {
                        if (err.Exception is PublicIdSerializationException pubEx)
                        {
                            PublicIdErrorRecorder.Record(context.HttpContext, pubEx.Code, pubEx.Message);
                        }
                    }
                }
                errors = PublicIdErrorRecorder.GetErrors(context.HttpContext);
                if (errors.Count == 0)
                    return;
            }

            var invalid = errors.FirstOrDefault(e => e.Code == PublicIdErrorCodes.InvalidId);
            if (invalid != null)
            {
                context.Result = new BadRequestObjectResult(new
                {
                    message = invalid.Message,
                    code = PublicIdErrorCodes.InvalidId
                });
                return;
            }

            // Byte-identical 404 body for both non-existent and foreign-workspace IDs (no existence oracle).
            context.Result = new NotFoundObjectResult(new
            {
                message = PublicIdErrorCodes.NotFoundMessage,
                code = PublicIdErrorCodes.UnknownId
            });
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }
    }
}
