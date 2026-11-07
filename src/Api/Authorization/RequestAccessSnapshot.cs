#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Api.Authorization;

internal sealed record RequestAccessSnapshot(
    int CompanyId, string UserId, int MemberId, CompanyMemberRole Role, CompanyMemberStatus Status,
    IReadOnlySet<string> Permissions, IReadOnlyList<string> RoleNames);

internal interface IRequestAccessLoader
{
    Task<RequestAccessSnapshot?> LoadAsync(int companyId, string userId, CancellationToken cancellationToken = default);
}

internal sealed class RequestAccessLoader(ApiDbContext db) : IRequestAccessLoader
{
    public async Task<RequestAccessSnapshot?> LoadAsync(int companyId, string userId, CancellationToken cancellationToken = default)
    {
        var rows = await (from member in db.CompanyMembers.AsNoTracking()
                          join company in db.Companies.AsNoTracking() on member.CompanyId equals company.Id
                          where member.CompanyId == companyId && member.UserId == userId
                          from grant in member.SecurityRoles.DefaultIfEmpty()
                          from permission in db.Set<Core.Domain.Security.SecurityRolePermission>()
                              .Where(p => grant != null && p.SecurityRoleId == grant.SecurityRoleId).DefaultIfEmpty()
                          select new
                          {
                              member.Id, member.Role, member.Status,
                              RoleName = grant != null && grant.SecurityRole != null ? grant.SecurityRole.Name : null,
                              PermissionName = permission != null && permission.SecurityPermission != null ? permission.SecurityPermission.Name : null
                          }).ToListAsync(cancellationToken);
        if (rows.Count == 0) return null;
        var first = rows[0];
        return new RequestAccessSnapshot(companyId, userId, first.Id, first.Role, first.Status,
            rows.Where(x => x.PermissionName != null).Select(x => x.PermissionName!).ToHashSet(StringComparer.OrdinalIgnoreCase),
            rows.Where(x => x.RoleName != null).Select(x => x.RoleName!).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }
}
