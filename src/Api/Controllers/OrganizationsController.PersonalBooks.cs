#nullable disable
using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Api.Filters;
using Api.Service;
using Core.Domain;
using Core.Domain.Auditing;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Api.Controllers;

public partial class OrganizationsController
{
    public sealed class CreatePersonalBooksRequest
    {
        public string Name { get; set; }
        public string Slug { get; set; }
        public string Currency { get; set; } = "USD";
        public DateTime? BooksStartDate { get; set; }
        [JsonConverter(typeof(StringEnumConverter))] public FiscalYearPattern? FiscalYearPattern { get; set; }
        public int? FiscalYearStartMonth { get; set; }
        public int? LinkWorkCompanyId { get; set; }
    }

    private IActionResult PersonalProblem(int status, string code, string detail) => StatusCode(status, new ProblemDetails
    {
        Status = status, Title = "Personal books setup", Detail = detail, Extensions = { ["code"] = code }
    });

    private IActionResult PersonalExists() => PersonalProblem(409, "personal_workspace_exists",
        "You already have personal books. Each person can have one.");

    private bool CanAddPersonalBooks()
    {
        if (string.IsNullOrWhiteSpace(UserId) || _db.PersonalWorkspaces.Any(p => p.OwnerUserId == UserId)) return false;
        var decision = _entitlements?.EvaluateForUser(UserId, new UserAction.CreateWorkspace(_plans.PersonalBooksPlanKey ?? _plans.DefaultPlanKey));
        return decision == null || (decision.Allowed && !decision.RequiresCheckout);
    }

    [TenantFree]
    [HttpPost("personal")]
    public async Task<IActionResult> CreatePersonal([FromBody] CreatePersonalBooksRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(UserId)) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name)) return Problem(statusCode: 400, title: "Company Name Required");
        var slug = WorkspaceService.NormalizeSlug(string.IsNullOrWhiteSpace(request.Slug) ? request.Name : request.Slug);
        var currency = (request.Currency ?? "USD").Trim().ToUpperInvariant();
        if (slug.Length == 0 || slug.Length > 100 || !System.Text.RegularExpressions.Regex.IsMatch(currency, "^[A-Z]{3}$"))
            return Problem(statusCode: 400, title: "Invalid workspace settings");
        var plan = _plans.PersonalBooksPlanKey ?? _plans.DefaultPlanKey;
        var options = ToOptions(new CreateOrganizationRequest
        {
            EntityType = EntityType.Individual, Currency = currency, BooksStartDate = request.BooksStartDate,
            FiscalYearPattern = request.FiscalYearPattern, FiscalYearStartMonth = request.FiscalYearStartMonth
        });
        var errors = CompanyAccountingPolicyRules.Validate(options, DateTime.UtcNow);
        if (errors.Count > 0) return Problem(statusCode: 400, title: "Invalid accounting policy", detail: string.Join(" ", errors));

        for (var attempt = 0; ; attempt++)
        {
            using var tx = _db.Database.BeginTransaction(IsolationLevel.Serializable);
            var policyScope = _policyTransaction?.BeginExternalScope();
            try
            {
                if (_db.PersonalWorkspaces.Any(p => p.OwnerUserId == UserId)) return PersonalExists();
                if (request.LinkWorkCompanyId is int workId)
                {
                    var invalid = ValidatePersonalLink(workId);
                    if (invalid != null) return invalid;
                }
                var decision = _entitlements?.EvaluateForUser(UserId, new UserAction.CreateWorkspace(plan));
                if (decision?.RequiresCheckout == true)
                {
                    HttpContext.RequestServices.GetService<ILogger<OrganizationsController>>()?
                        .LogError("Personal books setup incorrectly requires checkout.");
                    return Problem(statusCode: 500, title: "Personal books setup is misconfigured");
                }
                if (decision?.Allowed == false)
                {
                    if (string.Equals(decision.Code, "PERSONAL_WORKSPACE_EXISTS", StringComparison.OrdinalIgnoreCase)) return PersonalExists();
                    var problem = EntitlementProblemFactory.FromDecision(decision, _tenant, _entitlements.Options.SupportEmail);
                    return StatusCode(problem.Status ?? 403, problem);
                }
                if (_workspaces.SlugExists(slug)) return Problem(statusCode: 409, title: "Workspace Slug Already Taken");
                var company = _workspaces.Create(UserId, request.Name, slug, plan, currency, options: options);
                var owner = _db.CompanyMembers.Single(m => m.CompanyId == company.Id && m.UserId == UserId);
                _db.PersonalWorkspaces.Add(new PersonalWorkspace { OwnerUserId = UserId, CompanyId = company.Id, CreatedAtUtc = DateTime.UtcNow });
                _db.SaveChanges();
                var initialized = _workspaceInit.ApplyNewWorkspace(company.Id, UserId, owner.Id, new WorkspaceInitializationRequest
                {
                    Purpose = WorkspacePurpose.Personal, CollaborationIntent = WorkspaceCollaborationIntent.JustMe,
                    OwnerAccessPreset = OwnerAccessPreset.FullWorkspaceAccess,
                    InitialJournalWorkflow = WorkspaceApprovalRuleKind.SingleActorAllowed
                }, personalBooksSetup: true);
                if (!initialized.Applied)
                    return PersonalProblem(422, initialized.ReasonCode ?? "personal_books_setup_failed", initialized.Message);
                if (request.LinkWorkCompanyId is int linkId)
                {
                    var invalid = ValidatePersonalLink(linkId);
                    if (invalid != null) return invalid;
                    var link = new WorkspaceLink { OwnerUserId = UserId, WorkCompanyId = linkId, PersonalCompanyId = company.Id, CreatedAtUtc = DateTime.UtcNow };
                    _db.WorkspaceLinks.Add(link);
                    AuditPersonalLink(link, added: true);
                }
                tx.Commit();
                tx.Dispose();
                policyScope?.Committed();
                return StatusCode(201, Summary(company, CompanyMemberRole.Owner, initialized));
            }
            catch (Exception ex) when (IsPersonalUniqueViolation(ex))
            {
                tx.Rollback();
                policyScope?.RolledBack();
                _db.ChangeTracker.Clear();
                if (_db.PersonalWorkspaces.Any(p => p.OwnerUserId == UserId)) return PersonalExists();
                if (request.LinkWorkCompanyId is int linkedWorkId && _db.WorkspaceLinks.Any(l => l.WorkCompanyId == linkedWorkId))
                    return PersonalProblem(409, "work_workspace_already_linked", "This work workspace is already linked.");
                return Problem(statusCode: 409, title: "Workspace Slug Already Taken");
            }
            catch (Exception ex) when (attempt < 3 && IsPersonalTransactionConflict(ex))
            {
                tx.Rollback();
                policyScope?.RolledBack();
                _db.ChangeTracker.Clear();
                tx.Dispose();
                await Task.Delay(50 * (attempt + 1), ct);
            }
            finally { policyScope?.RolledBack(); }
        }
    }

    public sealed class PersonalLinkRequest { public int WorkCompanyId { get; set; } }

    [TenantFree]
    [HttpPost("personal/link")]
    public IActionResult LinkPersonal([FromBody] PersonalLinkRequest request)
    {
        using var tx = _db.Database.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            var personal = OwnedPersonalBooks();
            if (personal == null) return PersonalProblem(409, "personal_workspace_required", "Create your personal books first.");
            var invalid = ValidatePersonalLink(request.WorkCompanyId);
            if (invalid != null) return invalid;
            var link = new WorkspaceLink { OwnerUserId = UserId, WorkCompanyId = request.WorkCompanyId, PersonalCompanyId = personal.CompanyId, CreatedAtUtc = DateTime.UtcNow };
            _db.WorkspaceLinks.Add(link);
            AuditPersonalLink(link, added: true);
            tx.Commit();
            return NoContent();
        }
        catch (Exception ex) when (IsPersonalUniqueViolation(ex) || IsPersonalTransactionConflict(ex))
        {
            tx.Rollback();
            return PersonalProblem(409, "work_workspace_already_linked", "This work workspace is already linked.");
        }
    }

    [TenantFree]
    [HttpDelete("personal/link/{workCompanyId:int}")]
    public IActionResult UnlinkPersonal(int workCompanyId)
    {
        using var tx = _db.Database.BeginTransaction(IsolationLevel.Serializable);
        var personal = OwnedPersonalBooks();
        if (personal == null) return PersonalProblem(409, "personal_workspace_required", "Create your personal books first.");
        if (!OwnsLiveWorkspace(workCompanyId)) return Forbid();
        var link = _db.WorkspaceLinks.SingleOrDefault(l => l.OwnerUserId == UserId && l.WorkCompanyId == workCompanyId && l.PersonalCompanyId == personal.CompanyId);
        if (link == null) return NotFound();
        _db.WorkspaceLinks.Remove(link);
        AuditPersonalLink(link, added: false);
        tx.Commit();
        return NoContent();
    }

    private PersonalWorkspace OwnedPersonalBooks() => _db.PersonalWorkspaces.SingleOrDefault(p => p.OwnerUserId == UserId
        && _db.CompanyMembers.Any(m => m.CompanyId == p.CompanyId && m.UserId == UserId
            && m.Role == CompanyMemberRole.Owner && m.Status == CompanyMemberStatus.Active)
        && _db.Companies.Any(c => c.Id == p.CompanyId));

    private bool OwnsLiveWorkspace(int id) => _db.Companies.Any(c => c.Id == id)
        && _db.CompanyMembers.Any(m => m.CompanyId == id && m.UserId == UserId
            && m.Role == CompanyMemberRole.Owner && m.Status == CompanyMemberStatus.Active);

    private IActionResult ValidatePersonalLink(int workId)
    {
        if (!OwnsLiveWorkspace(workId)) return Forbid();
        if (_db.PersonalWorkspaces.Any(p => p.CompanyId == workId)
            || _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Any(s => s.CompanyId == workId && s.Purpose == WorkspacePurpose.Personal)
            || _db.WorkspaceLinks.Any(l => l.WorkCompanyId == workId))
            return PersonalProblem(409, "work_workspace_already_linked", "This workspace cannot be linked as work books or is already linked.");
        return null;
    }

    private void AuditPersonalLink(WorkspaceLink link, bool added)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new { link.WorkCompanyId, link.PersonalCompanyId });
        foreach (var id in new[] { link.WorkCompanyId, link.PersonalCompanyId })
        {
            var company = _db.Companies.Single(c => c.Id == id);
            using (_tenant.Impersonate(id, company.Slug, CompanyMemberRole.Owner))
            {
                _db.AuditLogs.Add(new AuditLog
                {
                    CompanyId = id, UserName = UserId, AuditEventDateUTC = DateTime.UtcNow,
                    AuditEventType = (int)(added ? AuditEventTypes.Added : AuditEventTypes.Deleted),
                    TableName = "__WorkspaceLink", RecordId = link.WorkCompanyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    NewValue = added ? payload : null, OriginalValue = added ? null : payload
                });
                _db.SaveChanges();
            }
        }
    }

    private static bool IsPersonalUniqueViolation(Exception ex) => ex switch
    {
        Microsoft.Data.Sqlite.SqliteException sqlite => sqlite.SqliteErrorCode == 19 && sqlite.SqliteExtendedErrorCode is 2067 or 1555,
        Npgsql.PostgresException postgres => postgres.SqlState == "23505",
        Microsoft.Data.SqlClient.SqlException sql => sql.Number is 2601 or 2627,
        _ => ex.InnerException != null && IsPersonalUniqueViolation(ex.InnerException)
    };

    private static bool IsPersonalTransactionConflict(Exception ex) => ex switch
    {
        Microsoft.Data.Sqlite.SqliteException sqlite => sqlite.SqliteErrorCode is 5 or 6,
        Npgsql.PostgresException postgres => postgres.SqlState is "40001" or "40P01",
        _ => ex.InnerException != null && IsPersonalTransactionConflict(ex.InnerException)
    };
}
