using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Api.Service.D1Sync
{
    public class D1SyncMiddleware
    {
        private const int MaxHeaderPayloadLength = 32768; // 32 KB
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
                        var fullJson = JsonSerializer.Serialize(statements);
                        var fullBytes = Encoding.UTF8.GetBytes(fullJson);
                        var fullBase64 = Convert.ToBase64String(fullBytes);

                        if (fullBase64.Length <= MaxHeaderPayloadLength)
                        {
                            context.Response.Headers["x-d1-sync-sql"] = fullBase64;
                        }
                        else
                        {
                            var chunks = SplitStatementsIntoChunks(statements, MaxHeaderPayloadLength);
                            for (int i = 0; i < chunks.Count; i++)
                            {
                                context.Response.Headers[$"x-d1-sync-sql-{i}"] = chunks[i];
                            }
                            context.Response.Headers["x-d1-sync-sql-count"] = chunks.Count.ToString();
                        }
                    }
                }
                return Task.CompletedTask;
            });

            await _next(context);
        }

        // Note: Splits statements on statement boundaries. If a single statement's serialized base64
        // payload exceeds maxChunkBase64Length (32 KB), it will produce one oversized chunk (acceptable for now;
        // accounting transaction rows are well below 32 KB).
        public static List<string> SplitStatementsIntoChunks(IReadOnlyList<string> statements, int maxChunkBase64Length = MaxHeaderPayloadLength)
        {
            var chunks = new List<string>();
            if (statements == null || statements.Count == 0)
            {
                return chunks;
            }

            var currentChunk = new List<string>();

            foreach (var statement in statements)
            {
                currentChunk.Add(statement);
                var json = JsonSerializer.Serialize(currentChunk);
                var byteCount = Encoding.UTF8.GetByteCount(json);
                var b64Length = ((byteCount + 2) / 3) * 4;

                if (b64Length > maxChunkBase64Length && currentChunk.Count > 1)
                {
                    currentChunk.RemoveAt(currentChunk.Count - 1);
                    var chunkJson = JsonSerializer.Serialize(currentChunk);
                    chunks.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(chunkJson)));

                    currentChunk = new List<string> { statement };
                }
            }

            if (currentChunk.Count > 0)
            {
                var chunkJson = JsonSerializer.Serialize(currentChunk);
                chunks.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(chunkJson)));
            }

            return chunks;
        }
    }
}
