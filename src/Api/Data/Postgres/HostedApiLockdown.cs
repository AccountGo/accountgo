using System;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Api.Data.Postgres
{
    /// <summary>
    /// Supabase publishes the <c>public</c> schema through its Data API and, by default, grants the <c>anon</c> and
    /// <c>authenticated</c> API roles full rights on every new table there. AccountGo never uses that API, so take those
    /// rights away (now and for future tables) and turn on row-level security as a second barrier. The API connects as
    /// the table owner, which RLS does not restrict. Does nothing on a Postgres without those roles.
    /// </summary>
    public static class HostedApiLockdown
    {
        private const string Sql = @"
DO $$
DECLARE
    api_roles text;
    t record;
BEGIN
    SELECT string_agg(quote_ident(rolname), ', ') INTO api_roles
    FROM pg_roles WHERE rolname IN ('anon', 'authenticated');
    IF api_roles IS NULL THEN
        RETURN;
    END IF;

    EXECUTE format('REVOKE ALL ON ALL TABLES IN SCHEMA public FROM %s', api_roles);
    EXECUTE format('REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM %s', api_roles);
    EXECUTE format('REVOKE ALL ON ALL FUNCTIONS IN SCHEMA public FROM %s', api_roles);
    EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON TABLES FROM %s', api_roles);
    EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON SEQUENCES FROM %s', api_roles);
    EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON FUNCTIONS FROM %s', api_roles);

    FOR t IN SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE n.nspname = 'public' AND c.relkind = 'r' AND NOT c.relrowsecurity
    LOOP
        EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', t.relname);
    END LOOP;
END $$;";

        public static void Apply(DbContext context)
        {
            context.Database.ExecuteSqlRaw(Sql);
            Console.WriteLine("✓ Hosted Data API access to public tables revoked (if present).");
        }
    }
}
