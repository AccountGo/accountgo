#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Api.Data;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Auditing;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Api.Service;

public sealed record WorkspaceSetupAccount(string Code, string Name, decimal Balance);

/// <summary>Read-only facts an operator needs before correcting a workspace's setup.</summary>
public sealed record WorkspaceSetupSnapshot(
    int CompanyId,
    string Slug,
    string Name,
    string Purpose,
    string CollaborationIntent,
    string EntityType,
    int PostedJournals,
    int ClosedFinancialYears,
    bool IsPersonalBooks,
    bool IsLinkedAsWork,
    int LinkedEntryPairs,
    IReadOnlyList<WorkspaceSetupAccount> EquityAccounts,
    IReadOnlyList<string> Blockers);

public sealed record WorkspaceSetupCorrectionResult(bool Applied, string? Code, string? Message, WorkspaceSetupSnapshot? Before, WorkspaceSetupSnapshot? After);

/// <summary>
/// Operator-only correction of a workspace that was set up as personal finances but is really a freelancer's
/// work books. It changes the setup only: purpose becomes Business and the business type becomes Sole proprietor,
/// using the same business-type change the accounting policy endpoint applies (close target and equity labels).
/// If the workspace was recorded as its owner's personal books, that record is released so the owner can create
/// new personal books; a workspace with links or linked entries is refused.
/// Posted entries, balances, members, plan and billing are never touched. Not exposed to workspace users.
/// </summary>
public sealed class WorkspaceSetupCorrectionService
{
    public const string AuditTable = "__WorkspaceSetupCorrection";
    private readonly ApiDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly CompanyAccountingPolicyService _policy;
    private readonly IWorkspacePolicyTransaction _transaction;

    public WorkspaceSetupCorrectionService(ApiDbContext db, ITenantContext tenant, CompanyAccountingPolicyService policy,
        IWorkspacePolicyTransaction transaction)
    {
        _db = db; _tenant = tenant; _policy = policy; _transaction = transaction;
    }

    public WorkspaceSetupSnapshot? Describe(int companyId)
    {
        var company = _db.Companies.IgnoreQueryFilters().AsNoTracking().SingleOrDefault(c => c.Id == companyId && c.DeletedAt == null);
        if (company == null) return null;
        using (_tenant.Impersonate(companyId, company.Slug, CompanyMemberRole.Owner))
            return Snapshot(company);
    }

    public WorkspaceSetupCorrectionResult ConvertPersonalToFreelance(int companyId, string operatorId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return new(false, "reason_required", "Give a reason for the correction.", null, null);
        var company = _db.Companies.IgnoreQueryFilters().SingleOrDefault(c => c.Id == companyId && c.DeletedAt == null);
        if (company == null) return new(false, "company_not_found", "Workspace not found.", null, null);

        using (_tenant.Impersonate(companyId, company.Slug, CompanyMemberRole.Owner))
        {
            var before = Snapshot(company);
            if (before.Blockers.Count > 0)
                return new(false, "setup_correction_blocked", string.Join(" ", before.Blockers), before, null);

            var outcome = _transaction.Execute(companyId, _ =>
            {
                // A workspace recorded as personal books stops being personal books; the owner's slot is freed.
                var personalBooks = _db.PersonalWorkspaces.Where(p => p.CompanyId == companyId).ToList();
                _db.PersonalWorkspaces.RemoveRange(personalBooks);

                var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == companyId);
                settings.Purpose = WorkspacePurpose.Business;
                settings.PolicyRevision++;

                var setting = _db.CompanySettings.IgnoreQueryFilters().Single(s => s.CompanyId == companyId);
                if (setting.EntityType != EntityType.SoleProprietor)
                {
                    var change = _policy.Apply(setting, new AccountingPolicyChange { EntityType = EntityType.SoleProprietor }, DateTime.UtcNow);
                    if (!change.Ok)
                        throw new InvalidOperationException(change.ClosedYearsConflict
                            ? "Reopen closed years before changing the business type."
                            : string.Join(" ", change.Errors));
                }

                _db.AuditLogs.Add(new AuditLog
                {
                    CompanyId = companyId, UserName = operatorId, AuditEventDateUTC = DateTime.UtcNow,
                    AuditEventType = (int)AuditEventTypes.Modified, TableName = AuditTable,
                    RecordId = companyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    OriginalValue = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        purpose = before.Purpose, entityType = before.EntityType,
                        personalBooksOwner = personalBooks.Select(p => p.OwnerUserId).FirstOrDefault()
                    }),
                    NewValue = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        purpose = nameof(WorkspacePurpose.Business), entityType = nameof(EntityType.SoleProprietor),
                        reason = reason.Trim(), by = "operator"
                    })
                });
                _db.SaveChanges();
                return true;
            });

            _db.ChangeTracker.Clear();
            var refreshed = _db.Companies.IgnoreQueryFilters().AsNoTracking().Single(c => c.Id == companyId);
            return new(outcome, null, null, before, Snapshot(refreshed));
        }
    }

    private WorkspaceSetupSnapshot Snapshot(Company company)
    {
        var id = company.Id;
        var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().AsNoTracking().SingleOrDefault(s => s.CompanyId == id);
        var entityType = _db.CompanySettings.IgnoreQueryFilters().AsNoTracking().Where(s => s.CompanyId == id)
            .Select(s => (EntityType?)s.EntityType).SingleOrDefault();
        var posted = _db.GeneralLedgerHeaders.IgnoreQueryFilters().Count(h => h.CompanyId == id);
        var closedYears = _db.FinancialYears.IgnoreQueryFilters().Count(fy => fy.CompanyId == id && fy.ClosedAt != null);
        var isPersonalBooks = _db.PersonalWorkspaces.Any(p => p.CompanyId == id);
        var linkedAsWork = _db.WorkspaceLinks.Any(l => l.WorkCompanyId == id || l.PersonalCompanyId == id);
        var pairs = _db.LinkedEntryPairs.Count(p => p.SourceCompanyId == id || p.MirrorCompanyId == id);
        var equity = _db.Accounts.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.CompanyId == id && a.AccountCode.StartsWith("30"))
            .OrderBy(a => a.AccountCode)
            .Select(a => new { a.Id, a.AccountCode, a.AccountName })
            .ToList()
            .Select(a => new WorkspaceSetupAccount(a.AccountCode, a.AccountName,
                _db.GeneralLedgerLines.IgnoreQueryFilters().Where(l => l.CompanyId == id && l.AccountId == a.Id)
                    .Sum(l => (decimal?)(l.DrCr == DrOrCrSide.Dr ? l.Amount : -l.Amount)) ?? 0m))
            .ToList();

        var blockers = new List<string>();
        if (settings == null) blockers.Add("The workspace has no setup record.");
        else if (settings.Purpose != WorkspacePurpose.Personal) blockers.Add("Only a workspace set up for personal finances can be corrected to freelance books.");
        if (entityType is not (EntityType.Individual or EntityType.SoleProprietor)) blockers.Add("The business type must be Individual or Sole proprietor.");
        if (closedYears > 0 && entityType != EntityType.SoleProprietor) blockers.Add("Reopen closed years before changing the business type.");
        if (linkedAsWork) blockers.Add("Unlink this workspace first.");
        if (pairs > 0) blockers.Add("This workspace has linked entries.");
        if (!equity.Any(a => a.Code == "30800")) blockers.Add("Account 30800 (drawings) is missing from the chart.");

        return new WorkspaceSetupSnapshot(id, company.Slug, company.Name,
            settings?.Purpose.ToString() ?? "Unspecified", settings?.CollaborationIntent.ToString() ?? "Unspecified",
            entityType?.ToString() ?? "Unspecified", posted, closedYears, isPersonalBooks, linkedAsWork, pairs, equity, blockers);
    }
}
