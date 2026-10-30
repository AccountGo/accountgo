using Api.Data.Postgres;
using Npgsql;
using Xunit;

namespace AccountGo.BackendTests
{
    public class PostgresConnectionStringTests
    {
        [Fact]
        public void Uri_BecomesAnNpgsqlConnectionString_WithTls()
        {
            var normalized = PostgresConnectionString.Normalize(
                "postgresql://postgres.abc:p%40ss%3Bword@aws-0-us-east-1.pooler.supabase.com:5432/accountgo");
            var builder = new NpgsqlConnectionStringBuilder(normalized);

            Assert.Equal("aws-0-us-east-1.pooler.supabase.com", builder.Host);
            Assert.Equal(5432, builder.Port);
            Assert.Equal("accountgo", builder.Database);
            Assert.Equal("postgres.abc", builder.Username);
            Assert.Equal("p@ss;word", builder.Password);
            Assert.Equal(SslMode.Require, builder.SslMode);
        }

        [Fact]
        public void Uri_QueryOptions_AreKept()
        {
            var builder = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Normalize(
                "\"postgres://u:p@db.example:6543/app?sslmode=verify-full&Maximum%20Pool%20Size=7\""));

            Assert.Equal(6543, builder.Port);
            Assert.Equal(SslMode.VerifyFull, builder.SslMode);
            Assert.Equal(7, builder.MaxPoolSize);
        }

        [Fact]
        public void KeywordForm_IsUnchanged()
        {
            const string value = "Host=localhost;Database=accountgo;Username=postgres";
            Assert.Equal(value, PostgresConnectionString.Normalize(value));
        }
    }
}
