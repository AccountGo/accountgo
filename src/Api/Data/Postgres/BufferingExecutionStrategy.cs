using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Api.Data.Postgres
{
    /// <summary>
    /// Runs each operation once, like the default strategy, but reports <see cref="RetriesOnFailure"/> so EF buffers
    /// every query result. Services stream one query while running others inside the loop; SQLite allows several open
    /// readers on a connection, Postgres does not ("A command is already in progress"). User transactions stay allowed
    /// because nothing is ever retried.
    /// </summary>
    public sealed class BufferingExecutionStrategy : IExecutionStrategy
    {
        private readonly DbContext _context;

        public BufferingExecutionStrategy(ExecutionStrategyDependencies dependencies)
        {
            _context = dependencies.CurrentContext.Context;
        }

        public bool RetriesOnFailure => true;

        public TResult Execute<TState, TResult>(TState state, Func<DbContext, TState, TResult> operation,
            Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded) =>
            operation(_context, state);

        public Task<TResult> ExecuteAsync<TState, TResult>(TState state,
            Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
            Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
            CancellationToken cancellationToken = default) =>
            operation(_context, state, cancellationToken);
    }
}
