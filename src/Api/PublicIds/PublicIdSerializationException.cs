// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Api.PublicIds
{
    public static class PublicIdErrorCodes
    {
        public const string InvalidId = "invalid_id";
        public const string UnknownId = "unknown_id";
        public const string NotFoundMessage = "The requested record was not found.";
        internal const string HttpContextErrorsKey = "__AccountGo_PublicIdErrors";
    }

    public sealed record PublicIdBindingError(string Code, string Message, string? ParameterName = null);

    public sealed class PublicIdSerializationException : Exception
    {
        public string Code { get; }

        public PublicIdSerializationException(string code, string message) : base(message)
        {
            Code = code;
        }
    }

    public static class PublicIdErrorRecorder
    {
        public static void Record(HttpContext? httpContext, string code, string message, string? parameterName = null)
        {
            if (httpContext == null)
                return;

            if (!httpContext.Items.TryGetValue(PublicIdErrorCodes.HttpContextErrorsKey, out var existing)
                || existing is not List<PublicIdBindingError> list)
            {
                list = new List<PublicIdBindingError>();
                httpContext.Items[PublicIdErrorCodes.HttpContextErrorsKey] = list;
            }
            list.Add(new PublicIdBindingError(code, message, parameterName));
        }

        public static IReadOnlyList<PublicIdBindingError> GetErrors(HttpContext? httpContext)
        {
            if (httpContext != null
                && httpContext.Items.TryGetValue(PublicIdErrorCodes.HttpContextErrorsKey, out var existing)
                && existing is List<PublicIdBindingError> list)
            {
                return list;
            }
            return Array.Empty<PublicIdBindingError>();
        }
    }
}
