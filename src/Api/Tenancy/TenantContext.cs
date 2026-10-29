#nullable enable
using System;
using System.Collections.Generic;
using Core.Domain;
using Core.Tenancy;

namespace Api.Tenancy
{
    public class TenantContext : ITenantContext
    {
        private class TenantState
        {
            public int? CompanyId { get; set; }
            public string? CompanySlug { get; set; }
            public CompanyMemberRole? Role { get; set; }
            public IReadOnlySet<string> Permissions { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private readonly Stack<TenantState> _impersonationStack = new();
        private readonly TenantState _resolvedState = new();

        public int? CompanyId => CurrentState.CompanyId;
        public string? CompanySlug => CurrentState.CompanySlug;
        public CompanyMemberRole? Role => CurrentState.Role;
        public IReadOnlySet<string> Permissions => CurrentState.Permissions;
        public bool IsResolved => CurrentState.CompanyId.HasValue;

        private TenantState CurrentState => _impersonationStack.Count > 0 ? _impersonationStack.Peek() : _resolvedState;

        public void SetTenant(int companyId, string slug, CompanyMemberRole role, IEnumerable<string> permissions)
        {
            _resolvedState.CompanyId = companyId;
            _resolvedState.CompanySlug = slug;
            _resolvedState.Role = role;
            _resolvedState.Permissions = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
        }

        public void Clear()
        {
            _resolvedState.CompanyId = null;
            _resolvedState.CompanySlug = null;
            _resolvedState.Role = null;
            _resolvedState.Permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _impersonationStack.Clear();
        }

        public int RequireCompanyId()
        {
            if (!CompanyId.HasValue)
            {
                throw new TenantNotResolvedException("Tenant context is not resolved for this operation.");
            }
            return CompanyId.Value;
        }

        public IDisposable Impersonate(int companyId, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null)
        {
            var state = new TenantState
            {
                CompanyId = companyId,
                CompanySlug = slug ?? $"org-{companyId}",
                Role = role ?? CompanyMemberRole.Owner,
                Permissions = permissions ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            };
            _impersonationStack.Push(state);
            return new ImpersonationScope(this);
        }

        private void PopImpersonation()
        {
            if (_impersonationStack.Count > 0)
            {
                _impersonationStack.Pop();
            }
        }

        private sealed class ImpersonationScope : IDisposable
        {
            private readonly TenantContext _context;
            private bool _disposed;

            public ImpersonationScope(TenantContext context)
            {
                _context = context;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _context.PopImpersonation();
                    _disposed = true;
                }
            }
        }
    }
}
