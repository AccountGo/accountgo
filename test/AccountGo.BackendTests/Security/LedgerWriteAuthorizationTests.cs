using Api.Data;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Security;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.Financial;
using Xunit;

namespace AccountGo.BackendTests.Security;

public sealed class LedgerWriteAuthorizationTests(AccessControlConformanceTests.Fixture fixture)
    : IClassFixture<AccessControlConformanceTests.Fixture>
{
    [Theory]
    [InlineData(DocumentTypes.SalesInvoice, false)]
    [InlineData(DocumentTypes.PurchaseInvoice, false)]
    [InlineData(DocumentTypes.BankTransaction, false)]
    [InlineData(DocumentTypes.JournalEntry, false)]
    [InlineData(DocumentTypes.SalesInvoice, true)]
    [InlineData(DocumentTypes.JournalEntry, true)]
    public async Task LedgerWrite_PeriodClosePermission_DoesNotAuthorizeUnrelatedWrites(DocumentTypes documentType, bool closingScope)
    {
        var slug = "ledger-auth-" + Guid.NewGuid().ToString("N")[..6];
        var owner = await fixture.CreateUser($"owner-{slug}@example.com");
        var companyId = await fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "Ledger authorization", slug);
        string roleName = "Period closer " + slug;
        using (var setup = fixture.Factory.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<ApiDbContext>();
            var role = new SecurityRole { Name = roleName, DisplayName = roleName };
            role.Permissions.Add(new SecurityRolePermission
            {
                SecurityPermissionId = db.SecurityPermissions.Single(p => p.Name == AccessOperationCatalog.FinancialsPeriodsClose).Id
            });
            db.SecurityRoles.Add(role);
            await db.SaveChangesAsync();
        }
        await fixture.AssignFunctionalRolesDirectAsync(companyId, owner.Id, new[] { roleName });

        using var scope = fixture.Factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var actorScope = tenant.ImpersonateUser(companyId, owner.Id, slug, CompanyMemberRole.Owner);
        var authorization = scope.ServiceProvider.GetRequiredService<IAccessAuthorizationService>();
        Assert.True(authorization.Authorize(AccessOperationCatalog.FinancialsPeriodsClose).Allowed);
        Assert.False(authorization.Authorize(AccessOperationCatalog.SalesInvoicesCreate).Allowed);
        Assert.False(authorization.Authorize(AccessOperationCatalog.FinancialsJournalPost).Allowed);
        var context = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var cashId = context.Accounts.Single(a => a.AccountCode == "10111").Id;
        var revenueId = context.Accounts.Single(a => a.AccountCode == "40100").Id;
        var header = new GeneralLedgerHeader { CompanyId = companyId, Date = DateTime.UtcNow.Date, DocumentType = documentType };
        header.GeneralLedgerLines.Add(new GeneralLedgerLine { CompanyId = companyId, AccountId = cashId, DrCr = DrOrCrSide.Dr, Amount = 10m });
        header.GeneralLedgerLines.Add(new GeneralLedgerLine { CompanyId = companyId, AccountId = revenueId, DrCr = DrOrCrSide.Cr, Amount = 10m });
        var financial = scope.ServiceProvider.GetRequiredService<IFinancialService>();
        using var closing = closingScope ? financial.AllowClosingEntries() : null;

        Assert.Throws<InvalidOperationException>(() => financial.SaveGeneralLedgerEntry(header));
        Assert.DoesNotContain(context.GeneralLedgerHeaders.AsNoTracking(), h => h.Id == header.Id);
        Assert.Throws<InvalidOperationException>(() => financial.UpdateGeneralLedgerHeader(header));
    }
}
