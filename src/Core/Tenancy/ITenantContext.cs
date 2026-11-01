#nullable enable
using System;
using System.Collections.Generic;
using Core.Authorization;
using Core.Domain;

namespace Core.Tenancy
{
    public interface ITenantContext
    {
        int? CompanyId { get; }            // null = no tenant resolved (auth/org-list endpoints)
        string? CompanySlug { get; }
        CompanyMemberRole? Role { get; }
        IReadOnlySet<string> Permissions { get; }
        bool IsResolved { get; }
        string? UserId => null;
        int? MemberId => null;
        ActorTrustDomain ActorDomain => IsResolved ? ActorTrustDomain.WorkspaceUser : ActorTrustDomain.Unauthenticated;
        long Revision => 0;
        long PermissionsRevision => Revision;
        int RequireCompanyId();            // throws TenantNotResolvedException if null
        IDisposable Impersonate(int companyId, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null);
        IDisposable ImpersonateUser(int companyId, string userId, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null)
            => Impersonate(companyId, slug, role, permissions);
    }
}
