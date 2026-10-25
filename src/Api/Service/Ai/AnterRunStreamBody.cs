#nullable enable
using System;
using System.Collections.Generic;

namespace Api.Service.Ai
{
    /// <summary>
    /// Builds the Anter run-stream JSON body. sessionId and userId must live in
    /// contextVariables (not only as top-level fields).
    /// </summary>
    public static class AnterRunStreamBody
    {
        public static Dictionary<string, object?> Build(
            string message,
            string userId,
            string? sessionId,
            IReadOnlyDictionary<string, string>? contextVariables)
        {
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            if (contextVariables != null)
            {
                foreach (var pair in contextVariables)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                    {
                        merged[pair.Key] = pair.Value;
                    }
                }
            }

            merged["userId"] = userId;
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                merged["sessionId"] = sessionId;
            }

            return new Dictionary<string, object?>
            {
                ["message"] = message ?? string.Empty,
                ["contextVariables"] = merged,
            };
        }
    }
}
