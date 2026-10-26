#nullable enable
using System;
using System.Collections.Generic;
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
        int RequireCompanyId();            // throws TenantNotResolvedException if null
        IDisposable Impersonate(int companyId, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null);
    }
}
