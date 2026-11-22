// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Data.Common;
using Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Api.DocumentCaptures;

public interface IDocumentCaptureAdmissionLock
{
    Task<IDbContextTransaction> AcquireCompanyAsync(int companyId, CancellationToken cancellationToken);
    Task<IDbContextTransaction> AcquirePurgeCompanyAsync(int companyId, CancellationToken cancellationToken);
    Task<IAsyncDisposable?> TryAcquirePurgeWorkerSingletonAsync(CancellationToken cancellationToken);
}

public sealed class DocumentCaptureAdmissionLock(ApiDbContext db) : IDocumentCaptureAdmissionLock
{
    private const long PurgeWorkerAdvisoryKey = 0x444F435057000001;
    public const int LockTimeoutMilliseconds = 500;

    public async Task<IDbContextTransaction> AcquireCompanyAsync(int companyId, CancellationToken cancellationToken)
        => await AcquireAsync(companyId, isPurge: false, cancellationToken).ConfigureAwait(false);

    public async Task<IDbContextTransaction> AcquirePurgeCompanyAsync(int companyId, CancellationToken cancellationToken)
        => await AcquireAsync(companyId, isPurge: true, cancellationToken).ConfigureAwait(false);

    private async Task<IDbContextTransaction> AcquireAsync(int companyId, bool isPurge, CancellationToken cancellationToken)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (var busyTimeout = connection.CreateCommand())
            {
                busyTimeout.CommandText = $"PRAGMA busy_timeout = {LockTimeoutMilliseconds};";
                await busyTimeout.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            SqliteTransaction transaction;
            try
            {
                transaction = connection.BeginTransaction(deferred: false);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
            {
                throw new DocumentCaptureAdmissionBusyException();
            }
            var attached = await db.Database.UseTransactionAsync(transaction, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The SQLite admission transaction could not be attached to the API context.");
            return new OwnedSqliteTransaction(attached, transaction);
        }

        var relationalTransaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '500ms'", cancellationToken).ConfigureAwait(false);
                    await LockCompanyRowAsync(companyId,
                        "SELECT \"Id\" FROM \"Company\" WHERE \"Id\" = @companyId{0} FOR UPDATE",
                        " AND \"DeletedAt\" IS NULL", isPurge, cancellationToken)
                        .ConfigureAwait(false);
                    await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '0ms'", cancellationToken).ConfigureAwait(false);
                }
                catch (PostgresException ex) when (ex.SqlState == "55P03")
                {
                    throw new DocumentCaptureAdmissionBusyException();
                }
            }
            else if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                await AcquireSqlServerCompanyRowAsync(companyId, isPurge, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                throw new NotSupportedException($"Document capture admission locking is not implemented for {provider}.");
            }

            return relationalTransaction;
        }
        catch
        {
            await relationalTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            await relationalTransaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class OwnedSqliteTransaction(IDbContextTransaction attached, SqliteTransaction underlying) : IDbContextTransaction
    {
        public Guid TransactionId => attached.TransactionId;
        public bool SupportsSavepoints => attached.SupportsSavepoints;
        public void Commit() => attached.Commit();
        public Task CommitAsync(CancellationToken cancellationToken = default) => attached.CommitAsync(cancellationToken);
        public void Rollback() => attached.Rollback();
        public Task RollbackAsync(CancellationToken cancellationToken = default) => attached.RollbackAsync(cancellationToken);
        public void CreateSavepoint(string name) => attached.CreateSavepoint(name);
        public Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default) =>
            attached.CreateSavepointAsync(name, cancellationToken);
        public void RollbackToSavepoint(string name) => attached.RollbackToSavepoint(name);
        public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default) =>
            attached.RollbackToSavepointAsync(name, cancellationToken);
        public void ReleaseSavepoint(string name) => attached.ReleaseSavepoint(name);
        public Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default) =>
            attached.ReleaseSavepointAsync(name, cancellationToken);

        public void Dispose()
        {
            try { attached.Dispose(); }
            finally { underlying.Dispose(); }
        }

        public async ValueTask DisposeAsync()
        {
            try { await attached.DisposeAsync().ConfigureAwait(false); }
            finally { await underlying.DisposeAsync().ConfigureAwait(false); }
        }
    }

    private async Task LockCompanyRowAsync(int companyId, string sqlTemplate, string livePredicate, bool isPurge,
        CancellationToken cancellationToken)
    {
        DbConnection connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = string.Format(System.Globalization.CultureInfo.InvariantCulture, sqlTemplate,
            isPurge ? string.Empty : livePredicate);
        var company = command.CreateParameter();
        company.ParameterName = "@companyId";
        company.Value = companyId;
        command.Parameters.Add(company);
        await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task AcquireSqlServerCompanyRowAsync(int companyId, bool isPurge, CancellationToken cancellationToken)
    {
        DbConnection connection = db.Database.GetDbConnection();
        await using var timeout = connection.CreateCommand();
        timeout.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        timeout.CommandText = "SET LOCK_TIMEOUT 500";
        await timeout.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LockCompanyRowAsync(companyId,
                "SELECT [Id] FROM [Company] WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE [Id] = @companyId{0}",
                " AND [DeletedAt] IS NULL", isPurge, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SqlException ex) when (ex.Number is 1222 or 1205)
        {
            throw new DocumentCaptureAdmissionBusyException();
        }
        finally
        {
            try
            {
                await using var reset = connection.CreateCommand();
                reset.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
                reset.CommandText = "SET LOCK_TIMEOUT -1";
                await reset.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (SqlException ex) when (ex.Number is 1205 or 1222)
            {
                // The transaction may already be aborted; preserve the original lock failure.
            }
        }
    }

    public async Task<IAsyncDisposable?> TryAcquirePurgeWorkerSingletonAsync(CancellationToken cancellationToken)
    {
        if (!(db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ?? false))
            return NoopAsyncDisposable.Instance;

        var connection = new NpgsqlConnection(db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("PostgreSQL connection string is unavailable."));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT pg_try_advisory_xact_lock(@lockKey)";
            command.Parameters.AddWithValue("lockKey", PurgeWorkerAdvisoryKey);
            if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true)
                return new PostgresAdvisoryLease(connection, transaction);

            await transaction.DisposeAsync().ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class PostgresAdvisoryLease(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public static readonly NoopAsyncDisposable Instance = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public sealed class DocumentCaptureAdmissionBusyException : Exception
{
}
