// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;

namespace Core.Domain
{
    /// <summary>
    /// Workspace-local calendar date from an injected UTC clock and <c>CompanySetting.Timezone</c>.
    /// Accounting dates stay date-only; this value is not a timestamp.
    /// </summary>
    public static class WorkspaceBusinessDate
    {
        public static DateTime Today(TimeProvider clock, string? timeZoneId) =>
            InZone(clock.GetUtcNow(), timeZoneId);

        public static DateTime InZone(DateTimeOffset utcNow, string? timeZoneId)
        {
            if (string.IsNullOrWhiteSpace(timeZoneId)
                || string.Equals(timeZoneId, "UTC", StringComparison.OrdinalIgnoreCase))
                return utcNow.UtcDateTime.Date;

            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return TimeZoneInfo.ConvertTime(utcNow, zone).Date;
            }
            catch (TimeZoneNotFoundException)
            {
                return utcNow.UtcDateTime.Date;
            }
            catch (InvalidTimeZoneException)
            {
                return utcNow.UtcDateTime.Date;
            }
        }
    }
}
