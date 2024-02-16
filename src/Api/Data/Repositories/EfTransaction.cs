using Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Threading.Tasks;

namespace Api.Data.Repositories
{
    public class EfTransaction(ApiDbContext _context) : IEfTransaction, IDisposable
    {
        private IDbContextTransaction _transaction;
        public IDisposable BeginTransaction()
        {
            _transaction = _context.Database.BeginTransaction();
            return _transaction;
        }

        public async Task<IDisposable> BeginTransactionAsync()
        {
            _transaction = await _context.Database.BeginTransactionAsync();
            return _transaction;
        }

        public void Commit()
        {
            _transaction.Commit();
        }

        public async Task CommitAsync()
        {
            await _transaction.CommitAsync();
        }

        public void Rollback()
        {
            _transaction.Rollback();
        }

        public async Task RollbackAsync()
        {
            await _transaction.RollbackAsync();
        }

        public void Dispose()
        {
            if(_transaction != null)
            {
                _transaction.Dispose();
            }
        }
    }
}
