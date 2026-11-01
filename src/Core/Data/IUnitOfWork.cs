// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;

namespace Core.Data
{
    /// <summary>
    /// A seam for the data layer's transaction so services can make several repository writes atomic without
    /// depending on the concrete DbContext. The Api layer implements this over its EF context.
    /// </summary>
    public interface IUnitOfWork
    {
        /// <summary>
        /// Opens a transaction at the strongest isolation (serializable) so concurrent runs can't interleave
        /// document creation and occurrence completion. Repository writes made while the returned scope is open
        /// are committed together by <see cref="ITransactionScope.Commit"/> or discarded on dispose-without-commit.
        /// </summary>
        ITransactionScope BeginSerializableTransaction();

        /// <summary>
        /// Drops all pending tracked changes. Used after a failed attempt so a fresh write (e.g. marking an
        /// occurrence failed) does not accidentally re-save the rolled-back document.
        /// </summary>
        void DiscardPendingChanges();
    }

    /// <summary>An open transaction. Commit to persist; dispose without committing to roll back.</summary>
    public interface ITransactionScope : IDisposable
    {
        void Commit();
    }
}
