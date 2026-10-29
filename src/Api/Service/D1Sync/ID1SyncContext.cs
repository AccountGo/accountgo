using System.Collections.Generic;

namespace Api.Service.D1Sync
{
    public interface ID1SyncContext
    {
        void AddStatement(string sql);
        IReadOnlyList<string> GetStatements();
        void Clear();
    }

    public class D1SyncContext : ID1SyncContext
    {
        private readonly List<string> _statements = new();

        public void AddStatement(string sql)
        {
            if (!string.IsNullOrWhiteSpace(sql))
            {
                _statements.Add(sql.Trim().TrimEnd(';') + ";");
            }
        }

        public IReadOnlyList<string> GetStatements() => _statements;

        public void Clear() => _statements.Clear();
    }
}
