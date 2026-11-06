// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Api.Service
{
    /// <summary>Strips secret-like keys from security-event metadata before persistence.</summary>
    public static class SecurityAuditMetadata
    {
        private static readonly string[] ForbiddenNameParts =
        {
            "Password", "Token", "Secret", "Code", "Otp"
        };

        public static object? Sanitize(object? metadata)
        {
            if (metadata is null) return null;
            try
            {
                var json = JsonSerializer.SerializeToNode(metadata);
                if (json is null) return null;
                Strip(json);
                return json;
            }
            catch (JsonException)
            {
                return new { note = "metadata_omitted" };
            }
        }

        public static string? Serialize(object? metadata)
        {
            var sanitized = Sanitize(metadata);
            if (sanitized is null) return null;
            return sanitized is JsonNode node
                ? node.ToJsonString()
                : JsonSerializer.Serialize(sanitized);
        }

        private static void Strip(JsonNode node)
        {
            if (node is JsonObject obj)
            {
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (IsForbidden(key))
                    {
                        obj.Remove(key);
                        continue;
                    }
                    if (obj[key] is JsonNode child)
                        Strip(child);
                }
            }
            else if (node is JsonArray array)
            {
                foreach (var item in array)
                {
                    if (item is not null)
                        Strip(item);
                }
            }
        }

        public static bool IsForbidden(string key) =>
            ForbiddenNameParts.Any(part => key.Contains(part, StringComparison.OrdinalIgnoreCase));
    }
}
