// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using Api.Data;
using Api.Data.Postgres;
using Api.Tenancy;
using Core.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AccountGo.ServicesTests;

/// <summary>
/// One disposable database for a concurrency test, on the provider named by the test.
/// <list type="bullet">
/// <item><see cref="Sqlite"/>: shared-cache in-memory SQLite; every writer opens its own connection.</item>
/// <item><see cref="Postgres"/>: disposable PostgreSQL database created from <c>ACCOUNTGO_TEST_POSTGRES</c>, using the
/// production context options (buffering execution strategy and the advisory write lock interceptor).</item>
/// <item><see cref="PostgresRowLockOnly"/>: the same PostgreSQL database without the advisory write lock, so the
/// <c>SELECT ... FOR UPDATE</c> coordination row and SERIALIZABLE isolation are the only serialization.</item>
/// </list>
/// Every call to <see cref="CreateContext"/> returns a context on its own connection.
/// </summary>
public sealed class PolicyRaceDatabase : IDisposable
{
    public const string Sqlite = "Sqlite";
    public const string Postgres = "Postgres";
    public const string PostgresRowLockOnly = "PostgresRowLockOnly";

    /// <summary>Attempts a concurrent writer makes before a transient provider error is treated as a failure.</summary>
    public const int MaxWriterAttempts = 10;

    private const string PostgresSerializationFailure = "40001";
    private const string PostgresDeadlockDetected = "40P01";
    private const string PostgresLockNotAvailable = "55P03";

    private readonly string _provider;
    private readonly SqliteConnection? _sqliteKeepAlive;
    private readonly string? _sqliteConnectionString;
    private readonly PostgresRaceDatabase? _postgres;

    private PolicyRaceDatabase(string provider)
    {
        _provider = provider;
        switch (provider)
        {
            case Sqlite:
                _sqliteConnectionString =
                    "DataSource=file:policy-race-" + Guid.NewGuid().ToString("N") + "?mode=memory&cache=shared";
                // Keep one connection open so the shared in-memory database stays alive.
                _sqliteKeepAlive = new SqliteConnection(_sqliteConnectionString);
                _sqliteKeepAlive.Open();
                break;
            case Postgres:
            case PostgresRowLockOnly:
                _postgres = PostgresRaceDatabase.Create();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown race provider.");
        }
    }

    /// <summary>Providers a Theory runs on: SQLite always, PostgreSQL variants only when ACCOUNTGO_TEST_POSTGRES is set.</summary>
    public static IEnumerable<object[]> ConfiguredProviders()
    {
        yield return new object[] { Sqlite };
        if (PostgresRaceDatabase.IsConfigured)
        {
            yield return new object[] { Postgres };
            yield return new object[] { PostgresRowLockOnly };
        }
    }

    public bool IsPostgres => _postgres != null;

    /// <summary>Npgsql connection string of the disposable PostgreSQL database; null on SQLite.</summary>
    public string? PostgresConnectionString => _postgres?.ConnectionString;

    /// <summary>Creates the database and the schema (EnsureCreated on SQLite, the Postgres migrations on PostgreSQL).</summary>
    public static PolicyRaceDatabase Create(string provider)
    {
        var database = new PolicyRaceDatabase(provider);
        try
        {
            using var context = database.CreateContext(new TenantContext());
            if (database.IsPostgres)
                context.Db.Database.Migrate();
            else
                context.Db.Database.EnsureCreated();
            return database;
        }
        catch
        {
            database.Dispose();
            throw;
        }
    }

    /// <summary>A new context on its own connection. Dispose it to release the connection.</summary>
    public PolicyRaceContext CreateContext(ITenantContext tenant)
    {
        if (_postgres != null)
        {
            var options = new DbContextOptionsBuilder<PostgresApiDbContext>()
                .UseNpgsql(_postgres.ConnectionString,
                    npgsql => npgsql.ExecutionStrategy(dependencies => new BufferingExecutionStrategy(dependencies)));
            if (_provider == Postgres)
                options.AddInterceptors(new WriteLockTransactionInterceptor());
            return new PolicyRaceContext(new PostgresApiDbContext(options.Options, tenant), connection: null);
        }

        var connection = new SqliteConnection(_sqliteConnectionString);
        connection.Open();
        var sqliteOptions = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options;
        return new PolicyRaceContext(new ApiDbContext(sqliteOptions, tenant), connection);
    }

    /// <summary>
    /// True for the errors a concurrent writer is expected to retry: SQLite lock errors, and PostgreSQL serialization
    /// failures, deadlocks and lock timeouts, including when EF wraps them in a <see cref="DbUpdateException"/>.
    /// </summary>
    public bool IsTransient(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            switch (current)
            {
                case SqliteException when _postgres == null:
                    return true;
                case PostgresException postgres:
                    return postgres.SqlState is PostgresSerializationFailure or PostgresDeadlockDetected or PostgresLockNotAvailable;
            }
        }

        return false;
    }

    public void Dispose()
    {
        _sqliteKeepAlive?.Dispose();
        _postgres?.Dispose();
    }
}

/// <summary>A context plus the connection it owns, if any.</summary>
public sealed class PolicyRaceContext : IDisposable
{
    private readonly DbConnection? _connection;

    internal PolicyRaceContext(ApiDbContext db, DbConnection? connection)
    {
        Db = db;
        _connection = connection;
    }

    public ApiDbContext Db { get; }

    public void Dispose()
    {
        Db.Dispose();
        _connection?.Dispose();
    }
}
