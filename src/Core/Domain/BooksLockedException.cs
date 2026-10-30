// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Globalization;

namespace Core.Domain
{
    /// <summary>A mutation targets a date on or before the workspace books-lock date.</summary>
    public sealed class BooksLockedException : InvalidOperationException
    {
        public DateTime LockedThrough { get; }

        public BooksLockedException(DateTime lockedThrough)
            : base(FormatMessage(lockedThrough))
        {
            LockedThrough = lockedThrough.Date;
        }

        public static string FormatMessage(DateTime lockedThrough) =>
            $"Books are locked through {lockedThrough.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}. An owner or admin can reopen them in Settings → Financial rules.";
    }
}
