using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Api.Middleware;

public sealed class CorrelationIdMiddleware
{
    public const string ItemKey = "AccountGo.CorrelationId";
    public const string HeaderName = "X-Request-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Activity.Current?.TraceId.ToString();
        if (string.IsNullOrWhiteSpace(correlationId)) correlationId = context.TraceIdentifier;
        context.Items[ItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        await _next(context);
    }
}
