#nullable enable
using System;
using System.Collections.Generic;

namespace Api.Service.Ai
{
    /// <summary>
    /// Builds the Anter run-stream JSON body. sessionId and userId must live in
    /// contextVariables (not only as top-level fields). Tenant identity fields are
    /// always overwritten from the server-resolved workspace.
    /// </summary>
    public static class AnterRunStreamBody
    {
        public const int MaxMessageLength = 8_000;

        private static readonly HashSet<string> ServerOwnedKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "userId",
            "sessionId",
            "organizationId",
            "companyId",
            "companySlug",
        };

        public static Dictionary<string, object?> Build(
            string message,
            string userId,
            string? sessionId,
            IReadOnlyDictionary<string, string>? contextVariables,
            string? companyId = null,
            string? companySlug = null)
        {
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            if (contextVariables != null)
            {
                foreach (var pair in contextVariables)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
                    {
                        continue;
                    }

                    // Client must not set identity / tenant keys.
                    if (ServerOwnedKeys.Contains(pair.Key))
                    {
                        continue;
                    }

                    merged[pair.Key] = pair.Value;
                }
            }

            merged["userId"] = userId;
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                merged["sessionId"] = sessionId;
            }

            if (!string.IsNullOrWhiteSpace(companyId))
            {
                merged["companyId"] = companyId;
                merged["organizationId"] = companyId;
            }

            if (!string.IsNullOrWhiteSpace(companySlug))
            {
                merged["companySlug"] = companySlug;
            }

            return new Dictionary<string, object?>
            {
                ["message"] = message ?? string.Empty,
                ["contextVariables"] = merged,
            };
        }
    }
}
