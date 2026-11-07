#nullable disable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using Api.Authorization;
using Api.Data;
using Api.Tenancy;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Security;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests;

public partial class MultiTenancyPhase3Tests
{
    [Theory]
    [InlineData("removed", HttpStatusCode.NotFound)]
    [InlineData("suspended", HttpStatusCode.NotFound)]
    [InlineData("roles", HttpStatusCode.Forbidden)]
    [InlineData("workspace", HttpStatusCode.NotFound)]
    [InlineData("user", HttpStatusCode.Unauthorized)]
    public async Task AccessRevocation_IsEnforcedOnNextRequest(string change, HttpStatusCode expected)
    {
        using (var first = await Send(HttpMethod.Get, "/api/Financials/Accounts")) Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var member = db.CompanyMembers.Include(x => x.SecurityRoles).Single(x => x.CompanyId == _companyId && x.UserId == _ownerId);
            switch (change)
            {
                case "removed": db.CompanyMemberSecurityRoles.RemoveRange(member.SecurityRoles); db.CompanyMembers.Remove(member); break;
                case "suspended": member.Status = CompanyMemberStatus.Suspended; break;
                case "roles": db.CompanyMemberSecurityRoles.RemoveRange(member.SecurityRoles); break;
                case "workspace": db.Companies.Single(x => x.Id == _companyId).DeletedAt = DateTime.UtcNow; break;
                case "user":
                    var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
                    var user = await identity.Users.SingleAsync(x => x.Id == _ownerId);
                    user.DeletedAt = DateTime.UtcNow;
                    await identity.SaveChangesAsync();
                    break;
            }
            await db.SaveChangesAsync();
        }
        using var second = await Send(HttpMethod.Get, "/api/Financials/Accounts");
        Assert.Equal(expected, second.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletedWorkspace_IsDeniedForActorOverride(bool suppliedMembership)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var impersonation = tenant.ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner);
        var company = await db.Companies.SingleAsync(x => x.Id == _companyId);
        company.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var authorization = new AccessAuthorizationService(tenant, scope.ServiceProvider.GetRequiredService<IEntitlementService>(), db);
        var actor = new AccessActorContext { TrustDomain = ActorTrustDomain.WorkspaceUser, UserId = _ownerId, CompanyId = _companyId,
            MembershipStatus = suppliedMembership ? CompanyMemberStatus.Active : null, WorkspaceRole = suppliedMembership ? CompanyMemberRole.Owner : null };
        var decision = await authorization.AuthorizeAsync(AccessOperationCatalog.ForPermission(SystemPermissions.FinancialsAccountsView), actorOverride: actor);
        Assert.False(decision.Allowed);
        Assert.Equal(404, decision.HttpStatus);
    }

    [Fact]
    public async Task InvalidatingAccessContext_SeesRoleRemovalInSameScope()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var impersonation = tenant.ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner);
        var authorization = new AccessAuthorizationService(tenant, scope.ServiceProvider.GetRequiredService<IEntitlementService>(), db);
        var operation = AccessOperationCatalog.ForPermission(SystemPermissions.FinancialsAccountsView);
        Assert.True((await authorization.AuthorizeAsync(operation)).Allowed);
        var grants = await db.CompanyMemberSecurityRoles.Where(x => x.CompanyMember.CompanyId == _companyId && x.CompanyMember.UserId == _ownerId).ToListAsync();
        db.CompanyMemberSecurityRoles.RemoveRange(grants);
        await db.SaveChangesAsync();
        authorization.InvalidateContextCache();
        Assert.False((await authorization.AuthorizeAsync(operation)).Allowed);
    }

    [Fact]
    public async Task ParallelRequests_DoNotShareUserAccess()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.CompanyMembers.Add(new CompanyMember { CompanyId = _companyId, UserId = _otherId, Role = CompanyMemberRole.Member, Status = CompanyMemberStatus.Active });
            await db.SaveChangesAsync();
        }
        var results = await Task.WhenAll(Send(HttpMethod.Get, "/api/Financials/Accounts", token: _ownerToken), Send(HttpMethod.Get, "/api/Financials/Accounts", token: _otherToken));
        try { Assert.Equal(HttpStatusCode.OK, results[0].StatusCode); Assert.Equal(HttpStatusCode.Forbidden, results[1].StatusCode); }
        finally { foreach (var response in results) response.Dispose(); }
    }
    [Fact]
    public async Task ApprovalEvidence_IsReadAgainAfterRevocationWithinScope()
    {
        var reviewer = await CreateUser("live-approval@example.test");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var impersonation = tenant.ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner);
        db.CompanyMembers.Add(new CompanyMember { CompanyId = _companyId, UserId = reviewer.Item2, Role = CompanyMemberRole.Admin, Status = CompanyMemberStatus.Active });
        await db.SaveChangesAsync();
        var approval = await AccessAuthorizationService.RecordAuthenticatedApprovalAsync(db, _companyId, reviewer.Item2, AccessOperationCatalog.FinancialsJournalPost, resourceId: "123");
        var authorization = new AccessAuthorizationService(tenant, scope.ServiceProvider.GetRequiredService<IEntitlementService>(), db);
        var resource = new AccessResourceContext { WorkspaceCompanyId = _companyId, ResourceCompanyId = _companyId, ResourceId = "123", WorkflowState = "Draft", PreparedByUserId = _ownerId, IndependentApproverUserId = reviewer.Item2, ApprovalRequestId = approval.RequestId };
        Assert.True((await authorization.AuthorizeAsync(AccessOperationCatalog.FinancialsJournalPost, resource)).Allowed);
        var grant = await db.AuditLogs.SingleAsync(x => x.RecordId == approval.RequestId && x.TableName == AccessAuthorizationService.ApprovalAuditTableName);
        db.AuditLogs.Add(ApprovalEvidence.State(grant, "Revoked", _ownerId));
        await db.SaveChangesAsync();
        Assert.False((await authorization.AuthorizeAsync(AccessOperationCatalog.FinancialsJournalPost, resource)).Allowed);
    }

    [Fact]
    public async Task MfaClaims_AreReevaluatedWhenAccessSnapshotIsReused()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var impersonation = tenant.ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner);
        var claims = new ClaimsIdentity(new[] { new Claim("sub", _ownerId), new Claim("mfa_verified_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)) }, "Bearer");
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(claims) } };
        var authorization = new AccessAuthorizationService(tenant, scope.ServiceProvider.GetRequiredService<IEntitlementService>(), db, accessor);
        Assert.True((await authorization.AuthorizeAsync(AccessOperationCatalog.WorkspaceOrganizationDelete)).Allowed);
        claims.RemoveClaim(claims.FindFirst("mfa_verified_at"));
        claims.AddClaim(new Claim("mfa_verified_at", DateTimeOffset.UtcNow.AddMinutes(-16).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        var decision = await authorization.AuthorizeAsync(AccessOperationCatalog.WorkspaceOrganizationDelete);
        Assert.False(decision.Allowed);
        Assert.Equal(AccessReasonCode.StepUpRequired, decision.ReasonCode);
    }

}
