// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;

namespace Services.Financial
{
    public interface IBooksLockService
    {
        /// <summary>
        /// Advances <c>BooksLockedThrough</c> to <paramref name="through"/> and records a
        /// <c>BooksLockChange</c> audit row. If the current lock is already on or after
        /// <paramref name="through"/>, does nothing and returns the current lock date.
        /// </summary>
        DateTime? Advance(DateTime through, string? reason, string actor);
    }

    public sealed class BooksLockValidationException : Exception
    {
        public BooksLockValidationException(string message) : base(message) { }
    }
}
