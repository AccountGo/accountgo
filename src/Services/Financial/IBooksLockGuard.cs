// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Core.Domain;

namespace Services.Financial
{
    /// <summary>
    /// Hard stop when a mutation targets a date on or before <see cref="CompanySetting.BooksLockedThrough"/>.
    /// No-op for invoicing-only workspaces (no Financials module) and when no lock is set.
    /// </summary>
    public interface IBooksLockGuard
    {
        /// <summary>
        /// Throws <see cref="BooksLockedException"/> when the date is on or before the lock
        /// and the workspace has the Financials module.
        /// </summary>
        void EnsureDateAllowed(DateTime date);
    }
}
