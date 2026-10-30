using System;
using Npgsql;

namespace AccountGo.BackendTests
{
    /// <summary>
    /// A throwaway Postgres database per test when ACCOUNTGO_TEST_POSTGRES holds a server connection string
    /// (e.g. "Host=127.0.0.1;Port=5432;Username=postgres;Password=..."); otherwise tests use SQLite.
    /// </summary>
    public sealed class PostgresTestDatabase : IDisposable
    {
        public const string EnvironmentVariable = "ACCOUNTGO_TEST_POSTGRES";

        private readonly string _serverConnectionString;
        private readonly string _name;

        public string ConnectionString { get; }

        private PostgresTestDatabase(string serverConnectionString)
        {
            _serverConnectionString = serverConnectionString;
            _name = "accountgo_test_" + Guid.NewGuid().ToString("N");
            Execute($"CREATE DATABASE \"{_name}\"");
            ConnectionString = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = _name }.ConnectionString;
        }

        public static bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable));

        public static PostgresTestDatabase? CreateIfConfigured() =>
            IsConfigured ? new PostgresTestDatabase(Environment.GetEnvironmentVariable(EnvironmentVariable)!) : null;

        public void Dispose()
        {
            NpgsqlConnection.ClearAllPools();
            Execute($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)");
        }

        private void Execute(string sql)
        {
            var builder = new NpgsqlConnectionStringBuilder(_serverConnectionString) { Database = "postgres", Pooling = false };
            using var connection = new NpgsqlConnection(builder.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}

namespace AccountGo.BackendTests
{
    /// <summary>A fact about the SQLite/D1 path (sync headers, SQLite schema upgrades); skipped on a Postgres run.</summary>
    public sealed class SqliteFactAttribute : Xunit.FactAttribute
    {
        public SqliteFactAttribute()
        {
            if (PostgresTestDatabase.IsConfigured)
                Skip = "SQLite/D1 only; this run uses Postgres.";
        }
    }

    /// <summary>A theory about the SQLite/D1 path; skipped on a Postgres run.</summary>
    public sealed class SqliteTheoryAttribute : Xunit.TheoryAttribute
    {
        public SqliteTheoryAttribute()
        {
            if (PostgresTestDatabase.IsConfigured)
                Skip = "SQLite/D1 only; this run uses Postgres.";
        }
    }
}
