// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Npgsql;

namespace AccountGo.ServicesTests;

/// <summary>
/// A throwaway PostgreSQL database per test, created from the server connection string held in
/// <c>ACCOUNTGO_TEST_POSTGRES</c> (for example <c>Host=127.0.0.1;Port=5432;Username=postgres;Password=...</c>).
/// Mirrors <c>AccountGo.BackendTests.PostgresTestDatabase</c>, which ServicesTests cannot reference.
/// </summary>
public sealed class PostgresRaceDatabase : IDisposable
{
    public const string EnvironmentVariable = "ACCOUNTGO_TEST_POSTGRES";

    private readonly string _serverConnectionString;
    private readonly string _databaseName;

    private PostgresRaceDatabase(string serverConnectionString)
    {
        _serverConnectionString = serverConnectionString;
        _databaseName = "accountgo_race_" + Guid.NewGuid().ToString("N");
        ExecuteOnServer($"CREATE DATABASE \"{_databaseName}\"");
        ConnectionString = new NpgsqlConnectionStringBuilder(serverConnectionString)
        {
            Database = _databaseName
        }.ConnectionString;
    }

    /// <summary>Connection string of the disposable database (pooled, so concurrent writers get separate connections).</summary>
    public string ConnectionString { get; }

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable));

    public static PostgresRaceDatabase Create()
    {
        var serverConnectionString = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(serverConnectionString))
            throw new InvalidOperationException(EnvironmentVariable + " is not set.");
        return new PostgresRaceDatabase(serverConnectionString);
    }

    public void Dispose()
    {
        NpgsqlConnection.ClearAllPools();
        ExecuteOnServer($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
    }

    private void ExecuteOnServer(string sql)
    {
        var builder = new NpgsqlConnectionStringBuilder(_serverConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        using var connection = new NpgsqlConnection(builder.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
