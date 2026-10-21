using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Api.Service.D1Sync
{
    public class D1SyncMiddleware
    {
        private readonly RequestDelegate _next;

        public D1SyncMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, ID1SyncContext syncContext)
        {
            context.Response.OnStarting(() =>
            {
                if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
                {
                    var statements = syncContext.GetStatements();
                    if (statements.Count > 0)
                    {
                        // JSON array (not newline-joined): values may contain line breaks.
                        var json = JsonSerializer.Serialize(statements);
                        var bytes = Encoding.UTF8.GetBytes(json);
                        context.Response.Headers["x-d1-sync-sql"] = Convert.ToBase64String(bytes);
                    }
                }
                return Task.CompletedTask;
            });

            await _next(context);
        }
    }
}
