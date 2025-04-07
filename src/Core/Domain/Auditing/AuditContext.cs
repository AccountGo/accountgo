using System.Threading;

namespace Core.Domain.Auditing
{
    public static class AuditContext
    {
        private static AsyncLocal<string> _currentUser = new AsyncLocal<string>();

        public static string CurrentUser
        {
            get { return _currentUser.Value ?? "Unknown"; }
            set { _currentUser.Value = value; }
        }
    }
}

