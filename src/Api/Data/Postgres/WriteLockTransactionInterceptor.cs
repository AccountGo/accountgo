using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Api.Data.Postgres
{
    /// <summary>
    /// Callers ask for <see cref="IsolationLevel.Serializable"/> to get SQLite's behaviour (BEGIN IMMEDIATE: the write
    /// lock is taken up front, so a second writer waits). Postgres SERIALIZABLE aborts the loser with 40001 instead.
    /// Give those callers a READ COMMITTED transaction that first takes one transaction-scoped advisory lock, so they
    /// queue the same way they do on SQLite.
    /// </summary>
    public sealed class WriteLockTransactionInterceptor : DbTransactionInterceptor
    {
        // Arbitrary, fixed key for the application's write lock.
        private const long WriteLockKey = 0x4143_474F_5752_4C4B;

        public override InterceptionResult<DbTransaction> TransactionStarting(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result)
        {
            if (eventData.IsolationLevel != IsolationLevel.Serializable)
                return result;

            var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT pg_advisory_xact_lock({WriteLockKey})";
            command.ExecuteNonQuery();
            return InterceptionResult<DbTransaction>.SuppressWithResult(transaction);
        }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.IsolationLevel != IsolationLevel.Serializable)
                return result;

            var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT pg_advisory_xact_lock({WriteLockKey})";
            await command.ExecuteNonQueryAsync(cancellationToken);
            return InterceptionResult<DbTransaction>.SuppressWithResult(transaction);
        }
    }
}
