using System;
using Npgsql;

namespace Api.Data.Postgres
{
    /// <summary>
    /// Accepts the <c>postgresql://user:password@host:port/database</c> URI that hosted Postgres consoles hand out, as
    /// well as Npgsql's <c>Host=...;Database=...</c> form (Npgsql itself only reads the latter).
    /// </summary>
    public static class PostgresConnectionString
    {
        public static string Normalize(string connectionString)
        {
            var value = connectionString.Trim().Trim('"');
            if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
                && !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            var uri = new Uri(value);
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
                Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')) is { Length: > 0 } db ? db : "postgres",
            };

            var userInfo = uri.UserInfo.Split(':', 2);
            if (userInfo[0].Length > 0)
                builder.Username = Uri.UnescapeDataString(userInfo[0]);
            if (userInfo.Length > 1)
                builder.Password = Uri.UnescapeDataString(userInfo[1]);

            // Query options use Npgsql keywords or libpq names (sslmode=require).
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                var key = Uri.UnescapeDataString(parts[0]);
                var option = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
                if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                    builder["SSL Mode"] = option.Replace("-", string.Empty); // verify-full -> VerifyFull
                else
                    builder[key] = option;
            }

            // Hosted Postgres requires TLS; a URI without sslmode still gets it.
            if (builder.SslMode is SslMode.Disable or SslMode.Allow or SslMode.Prefer)
                builder.SslMode = SslMode.Require;

            return builder.ConnectionString;
        }
    }
}
