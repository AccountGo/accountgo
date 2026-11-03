// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.IO;

namespace Api.Service
{
    /// <summary>
    /// Resolves and caches the deployed BUILD_SHA for health checks and internal synchronization.
    /// </summary>
    public static class BuildMetadata
    {
        private static string? _cachedSha;

        public static string GetBuildSha()
        {
            if (_cachedSha != null)
                return _cachedSha;

            string sha = "dev";
            var possiblePaths = new[]
            {
                "/app/BUILD_SHA",
                Path.Combine(AppContext.BaseDirectory, "BUILD_SHA"),
                Path.Combine(Directory.GetCurrentDirectory(), "BUILD_SHA"),
                Path.Combine(Directory.GetCurrentDirectory(), "src", "Api", "BUILD_SHA")
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    var content = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrEmpty(content))
                    {
                        sha = content;
                        break;
                    }
                }
            }

            _cachedSha = sha;
            return sha;
        }

        public static void SetBuildShaForTesting(string? sha)
        {
            _cachedSha = sha;
        }
    }
}
