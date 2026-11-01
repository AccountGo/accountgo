// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Data;
using Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Api.Data
{
    /// <summary>
    /// <see cref="IUnitOfWork"/> over the scoped <see cref="ApiDbContext"/>. Serializable matches the isolation
    /// the sales controllers already use for invoice numbering (BEGIN IMMEDIATE on SQLite), so the recurring
    /// run takes the write lock up front and its draft-save + issue + occurrence-completion commit as one unit.
    /// </summary>
    public sealed class EfUnitOfWork : IUnitOfWork
    {
        private readonly ApiDbContext _context;

        public EfUnitOfWork(ApiDbContext context)
        {
            _context = context;
        }

        public ITransactionScope BeginSerializableTransaction()
        {
            var tx = _context.Database.BeginTransaction(IsolationLevel.Serializable);
            return new EfTransactionScope(tx);
        }

        public void DiscardPendingChanges()
        {
            _context.ChangeTracker.Clear();
        }

        private sealed class EfTransactionScope : ITransactionScope
        {
            private readonly IDbContextTransaction _tx;
            private bool _committed;
            private bool _disposed;

            public EfTransactionScope(IDbContextTransaction tx) => _tx = tx;

            public void Commit()
            {
                _tx.Commit();
                _committed = true;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;

                if (!_committed)
                    _tx.Rollback();
                _tx.Dispose();
            }
        }
    }
}
