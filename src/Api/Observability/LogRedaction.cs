// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Api.Observability
{
    /// <summary>
    /// Helpers that keep PII and secrets out of text logs and console output.
    /// </summary>
    public static class LogRedaction
    {
        private static readonly Regex InviteTokenInPath = new(
            @"(/invite/)[A-Za-z0-9_-]{20,}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Pseudonymizes an address for logs as <c>email#</c> + the first 12 hex chars of the SHA-256 of the
        /// normalized (trimmed, lower-cased) address.
        /// </summary>
        public static string RedactEmail(string? email)
        {
            var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            return "email#" + Convert.ToHexString(hash).Substring(0, 12).ToLowerInvariant();
        }

        /// <summary>
        /// Replaces workspace invitation path tokens so console dumps cannot leak live invite URLs.
        /// </summary>
        public static string RedactInviteTokens(string? content)
        {
            if (string.IsNullOrEmpty(content))
                return content ?? string.Empty;
            return InviteTokenInPath.Replace(content, "$1[redacted]");
        }
    }
}
