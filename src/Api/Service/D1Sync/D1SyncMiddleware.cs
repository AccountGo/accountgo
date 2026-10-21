using System;
using System.Text;
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
                        var combinedSql = string.Join("\n", statements);
                        var bytes = Encoding.UTF8.GetBytes(combinedSql);
                        context.Response.Headers["x-d1-sync-sql"] = Convert.ToBase64String(bytes);
                    }
                }
                return Task.CompletedTask;
            });

            await _next(context);
        }
    }
}
