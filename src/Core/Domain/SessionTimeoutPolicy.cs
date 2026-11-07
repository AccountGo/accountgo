// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Domain
{
    /// <summary>
    /// Web sign-in session limits. A workspace picks its idle timeout; a user's session uses the strictest value
    /// across the workspaces they belong to, because one sign-in covers all of them.
    /// </summary>
    public static class SessionTimeoutPolicy
    {
        public const int DefaultIdleMinutes = 60;

        public static readonly IReadOnlyList<int> AllowedIdleMinutes = new[] { 15, 30, 60, 120, 240 };

        /// <summary>Default hard limit from sign-in; overridable with <c>Session:AbsoluteHours</c>.</summary>
        public const int DefaultAbsoluteHours = 12;

        /// <summary>
        /// The browser refreshes at most this often while the user is active. The server allows this long plus
        /// one minute past the idle limit so an active user is never refused before the browser's own deadline.
        /// </summary>
        public static readonly TimeSpan ActivityRefreshInterval = TimeSpan.FromMinutes(5);

        public static readonly TimeSpan ServerSlack = ActivityRefreshInterval + TimeSpan.FromMinutes(1);

        public static bool IsAllowed(int minutes) => AllowedIdleMinutes.Contains(minutes);

        /// <summary>Unknown stored values fall back to the nearest allowed bound.</summary>
        public static int Normalize(int minutes) =>
            Math.Clamp(minutes, AllowedIdleMinutes[0], AllowedIdleMinutes[AllowedIdleMinutes.Count - 1]);
    }
}
