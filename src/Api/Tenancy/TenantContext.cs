#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using Core.Authorization;
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
            public string? UserId { get; set; }
            public int? MemberId { get; set; }
            public ActorTrustDomain ActorDomain { get; set; } = ActorTrustDomain.Unauthenticated;
        }

        private readonly Stack<TenantState> _impersonationStack = new();
        private readonly TenantState _resolvedState = new();
        private long _revision;

        public int? CompanyId => CurrentState.CompanyId;
        public string? CompanySlug => CurrentState.CompanySlug;
        public CompanyMemberRole? Role => CurrentState.Role;
        public IReadOnlySet<string> Permissions => CurrentState.Permissions;
        public bool IsResolved => CurrentState.CompanyId.HasValue;
        public string? UserId => CurrentState.UserId;
        public int? MemberId => CurrentState.MemberId;
        public ActorTrustDomain ActorDomain => CurrentState.ActorDomain;
        public long Revision => Interlocked.Read(ref _revision);
        public long PermissionsRevision => Revision;

        private TenantState CurrentState => _impersonationStack.Count > 0 ? _impersonationStack.Peek() : _resolvedState;

        public void SetTenant(
            int companyId,
            string slug,
            CompanyMemberRole role,
            IEnumerable<string> permissions,
            string? userId = null,
            int? memberId = null,
            ActorTrustDomain actorDomain = ActorTrustDomain.WorkspaceUser)
        {
            _resolvedState.CompanyId = companyId;
            _resolvedState.CompanySlug = slug;
            _resolvedState.Role = role;
            _resolvedState.Permissions = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
            _resolvedState.UserId = userId;
            _resolvedState.MemberId = memberId;
            _resolvedState.ActorDomain = actorDomain;
            Interlocked.Increment(ref _revision);
        }

        public void Clear()
        {
            _resolvedState.CompanyId = null;
            _resolvedState.CompanySlug = null;
            _resolvedState.Role = null;
            _resolvedState.Permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _resolvedState.UserId = null;
            _resolvedState.MemberId = null;
            _resolvedState.ActorDomain = ActorTrustDomain.Unauthenticated;
            _impersonationStack.Clear();
            Interlocked.Increment(ref _revision);
        }

        public void BumpRevision()
        {
            Interlocked.Increment(ref _revision);
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
            => ImpersonateUser(companyId, _resolvedState.UserId ?? string.Empty, slug, role, permissions);

        public IDisposable ImpersonateUser(int companyId, string userId, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null)
        {
            var state = new TenantState
            {
                CompanyId = companyId,
                CompanySlug = slug ?? $"org-{companyId}",
                Role = role ?? CompanyMemberRole.Owner,
                Permissions = permissions ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                UserId = string.IsNullOrWhiteSpace(userId) ? _resolvedState.UserId : userId,
                MemberId = _resolvedState.MemberId,
                ActorDomain = ActorTrustDomain.WorkspaceUser
            };
            _impersonationStack.Push(state);
            Interlocked.Increment(ref _revision);
            return new ImpersonationScope(this);
        }

        private void PopImpersonation()
        {
            if (_impersonationStack.Count > 0)
            {
                _impersonationStack.Pop();
                Interlocked.Increment(ref _revision);
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
