using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Observability;
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
                        var prepSw = Stopwatch.StartNew();
                        var chunks = SplitStatementsIntoChunks(statements);
                        prepSw.Stop();
                        if (context.Items[ServerTimingAccumulator.HttpContextItemsKey] is ServerTimingAccumulator timing)
                            timing.SetD1Prep(prepSw.ElapsedMilliseconds);

                        context.Response.Headers["x-d1-sync-encoding"] = "gzip";
                        if (chunks.Count == 1)
                        {
                            context.Response.Headers["x-d1-sync-sql"] = chunks[0];
                        }
                        else
                        {
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

        // Compress once for the best ratio, then split the base64 stream. The worker joins before decoding.
        public static List<string> SplitStatementsIntoChunks(IReadOnlyList<string> statements, int maxChunkBase64Length = MaxHeaderPayloadLength)
        {
            if (maxChunkBase64Length <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxChunkBase64Length));
            var chunks = new List<string>();
            if (statements == null || statements.Count == 0)
            {
                return chunks;
            }

            string encoded;
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, true))
                {
                    var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(statements));
                    gzip.Write(bytes, 0, bytes.Length);
                }
                encoded = Convert.ToBase64String(output.ToArray());
            }
            for (var offset = 0; offset < encoded.Length; offset += maxChunkBase64Length)
                chunks.Add(encoded.Substring(offset, Math.Min(maxChunkBase64Length, encoded.Length - offset)));
            return chunks;
        }
    }
}
