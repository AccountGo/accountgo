#nullable disable
using System;
using System.Linq;
using System.Security.Claims;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Api.Filters;
using Api.Service;
using Core.Authorization;
using Core.Billing;
using Core.Domain;
using Core.Entitlements;
using Microsoft.AspNetCore.Http;
using Core.Domain.Financials;
using Core.Domain.Security;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Services.Billing;

namespace Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/organizations")]
    public partial class OrganizationsController : BaseController
    {
        private readonly ITenantContext _tenant;
        private readonly ApiDbContext _db;
        private readonly WorkspaceService _workspaces;
        private readonly CompanyAccountingPolicyService _accountingPolicy;
        private readonly IPlanCatalog _plans;
        private readonly Services.Financial.IFinancialService _financial;
        private readonly WorkspaceInitializationApplicator _workspaceInit;
        private readonly IWorkspaceOperatingPolicyService _operatingPolicy;
        private readonly Api.Options.WorkspaceExperienceOptions _experience;
        private readonly Core.Authorization.IWorkspacePolicyTransaction _policyTransaction;
        private readonly IWorkspaceReviewerEligibility _reviewerEligibility;
        private readonly IEntitlementService _entitlements;
        private readonly IBillingProvider _billing;
        private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;

        public OrganizationsController(
            ITenantContext tenant,
            ApiDbContext db,
            WorkspaceService workspaces,
            CompanyAccountingPolicyService accountingPolicy,
            IPlanCatalog plans,
            Services.Financial.IFinancialService financial,
            WorkspaceInitializationApplicator workspaceInit = null,
            IWorkspaceOperatingPolicyService operatingPolicy = null,
            Microsoft.Extensions.Options.IOptions<Api.Options.WorkspaceExperienceOptions> experienceOptions = null,
            Core.Authorization.IWorkspacePolicyTransaction policyTransaction = null,
            IWorkspaceReviewerEligibility reviewerEligibility = null,
            IEntitlementService entitlements = null,
            IBillingProvider billing = null)
        {
            _tenant = tenant;
            _db = db;
            _workspaces = workspaces;
            _accountingPolicy = accountingPolicy;
            _plans = plans;
            _financial = financial;
            _workspaceInit = workspaceInit;
            _operatingPolicy = operatingPolicy;
            _policyTransaction = policyTransaction;
            _reviewerEligibility = reviewerEligibility;
            _entitlements = entitlements;
            _billing = billing;
            _experience = experienceOptions?.Value != null
                ? Api.Options.WorkspaceExperienceOptions.FromEnvironment(experienceOptions.Value)
                : (workspaceInit?.Experience ?? new Api.Options.WorkspaceExperienceOptions());
        }

        [TenantFree]
        [HttpGet("setup-capabilities")]
        public IActionResult GetSetupCapabilities()
        {
            var options = Api.Options.WorkspaceExperienceOptions.FromEnvironment(_experience);
            return Ok(new
            {
                soloSetupEnabled = options.SoloSetupEnabled,
                personalPurposeEnabled = options.PersonalPurposeEnabled,
                controlledRecoveryEnabled = options.ControlledRecoveryEnabled,
                canAddPersonalBooks = CanAddPersonalBooks()
            });
        }

        [TenantFree]
        [HttpGet]
        public IActionResult List()
        {
            var rows = (from m in _db.CompanyMembers
                        join c in _db.Companies on m.CompanyId equals c.Id
                        where m.UserId == UserId && m.Status == CompanyMemberStatus.Active
                        orderby c.Name
                        select new { Company = c, m.Role }).ToList();
            var canAddPersonalBooks = CanAddPersonalBooks();
            return Ok(rows.Select(r => Summary(r.Company, r.Role, canAddPersonalBooks: canAddPersonalBooks)));
        }

        public class CreateOrganizationRequest
        {
            public string Name { get; set; } = "";
            public string ShortName { get; set; }
            public string Slug { get; set; }
            public string Plan { get; set; }
            public string Currency { get; set; } = "USD";
            /// <summary>Absolute return URL for billing-provider checkout when creating a paid workspace.</summary>
            public string ReturnUrl { get; set; }
            public string LegalName { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public ReportingFramework? ReportingFramework { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public AccountingBasis? AccountingBasis { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public FiscalYearPattern? FiscalYearPattern { get; set; }
            public int? FiscalYearStartMonth { get; set; }
            public DateTime? BooksStartDate { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public TaxRegime? TaxRegime { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public IndustryTemplate? IndustryTemplate { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public EntityType? EntityType { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public InventoryCostingMethod? InventoryCostingMethod { get; set; }
            public System.Collections.Generic.List<int> SecurityRoleIds { get; set; }

            // Additive About answers. Omitted values keep the legacy unclassified path (no automatic full-access grant).
            [JsonConverter(typeof(StringEnumConverter))] public WorkspacePurpose? WorkspacePurpose { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public WorkspaceCollaborationIntent? CollaborationIntent { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public OwnerAccessPreset? OwnerAccessPreset { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public WorkspaceApprovalRuleKind? InitialJournalWorkflow { get; set; }
        }

        /// <summary>Omitted policy fields fall back to the legacy defaults documented on <see cref="CompanyProvisionOptions"/>.</summary>
        private static CompanyProvisionOptions ToOptions(CreateOrganizationRequest request)
        {
            var options = new CompanyProvisionOptions
            {
                LegalName = request.LegalName,
                FiscalYearStartMonth = request.FiscalYearStartMonth,
                BooksStartDate = request.BooksStartDate?.Date
            };
            if (request.ReportingFramework is ReportingFramework framework) options.ReportingFramework = framework;
            // Cash basis framework implies Cash basis; every other framework implies Accrual unless the client says otherwise.
            options.AccountingBasis = request.AccountingBasis
                ?? (options.ReportingFramework == ReportingFramework.CashBasis ? AccountingBasis.Cash : AccountingBasis.Accrual);
            if (request.FiscalYearPattern is FiscalYearPattern pattern) options.FiscalYearPattern = pattern;
            if (request.TaxRegime is TaxRegime regime) options.TaxRegime = regime;
            if (request.IndustryTemplate is IndustryTemplate industry) options.IndustryTemplate = industry;
            if (request.EntityType is EntityType entityType) options.EntityType = entityType;
            if (request.InventoryCostingMethod is InventoryCostingMethod costing) options.InventoryCostingMethod = costing;
            return options;
        }

        [TenantFree]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateOrganizationRequest request, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(UserId))
                return Unauthorized();
            if (string.IsNullOrWhiteSpace(request.Name))
                return Problem(statusCode: 400, title: "Company Name Required", detail: "Company name is required.");
            var slug = WorkspaceService.NormalizeSlug(string.IsNullOrWhiteSpace(request.Slug) ? request.Name : request.Slug);
            var currency = (request.Currency ?? "USD").Trim().ToUpperInvariant();
            var requestedPlan = string.IsNullOrWhiteSpace(request.Plan) ? _plans.DefaultPlanKey : request.Plan;
            if (!_plans.TryResolvePlan(requestedPlan, out var resolvedPlan))
                return Problem(statusCode: 400, title: "Invalid workspace settings");
            var plan = resolvedPlan.Key;
            if (_plans.PersonalBooksPlanKey != null && string.Equals(plan, _plans.PersonalBooksPlanKey, StringComparison.OrdinalIgnoreCase))
                return PersonalProblem(400, "use_personal_books_setup", "Personal books are created from the personal books setup.");
            if (slug.Length == 0 || slug.Length > 100 || !System.Text.RegularExpressions.Regex.IsMatch(currency, "^[A-Z]{3}$"))
                return Problem(statusCode: 400, title: "Invalid workspace settings");

            EntitlementDecision createDecision = null;
            if (_entitlements != null)
            {
                createDecision = _entitlements.EvaluateForUser(UserId, new UserAction.CreateWorkspace(plan));
                if (!createDecision.Allowed)
                    throw EntitlementDecisions.ToException(createDecision);
            }

            var storePlan = plan;
            var pendingCheckout = createDecision?.RequiresCheckout == true;
            if (pendingCheckout)
            {
                if (!TryResolveFreeSelfServicePlan(resolvedPlan, out storePlan))
                    return Problem(statusCode: 400, title: "Invalid workspace settings",
                        detail: "No free self-service plan is available for this product.");
            }

            var options = ToOptions(request);
            // Personal purpose defaults to the Individual accounting profile; reachable only once personal is released.
            if (request.WorkspacePurpose == WorkspacePurpose.Personal
                && !request.EntityType.HasValue
                && WorkspaceInitializationScope.IsPersonalReleased(Api.Options.WorkspaceExperienceOptions.FromEnvironment(_experience)))
                options.EntityType = EntityType.Individual;
            var policyErrors = CompanyAccountingPolicyRules.Validate(options, DateTime.UtcNow);
            if (policyErrors.Count > 0)
                return Problem(statusCode: 400, title: "Invalid accounting policy", detail: string.Join(" ", policyErrors));
            var wantsNewExperience = request.WorkspacePurpose.HasValue || request.CollaborationIntent.HasValue || request.OwnerAccessPreset.HasValue;
            if (wantsNewExperience)
            {
                var experience = Api.Options.WorkspaceExperienceOptions.FromEnvironment(_experience);
                if (!experience.SoloSetupEnabled)
                {
                    return UnprocessableEntity(new ProblemDetails
                    {
                        Title = "Workspace initialization disabled",
                        Detail = "Solo/team workspace initialization is not enabled; workspace remains unclassified.",
                        Status = StatusCodes.Status422UnprocessableEntity,
                        Extensions = { ["reasonCode"] = WorkspacePolicyReasonCodes.SoloSetupDisabled }
                    });
                }

                if (!request.WorkspacePurpose.HasValue || !request.CollaborationIntent.HasValue)
                    return Problem(statusCode: 400, title: "About answers required",
                        detail: "Both workspacePurpose and collaborationIntent are required for the new workspace experience.");

                // Approved-journey scope gate at the request boundary: setup releases the Business solo
                // (Just me) and team (With others) journeys. Personal purpose is out of scope and is rejected
                // here (and again at initialization) so no caller can reach it.
                if (!WorkspaceInitializationScope.IsSupported(request.WorkspacePurpose.Value, request.CollaborationIntent.Value, WorkspaceInitializationScope.IsPersonalReleased(experience)))
                {
                    return UnprocessableEntity(new ProblemDetails
                    {
                        Title = "Workspace setup not available",
                        Detail = WorkspaceInitializationScope.UnsupportedScopeMessage,
                        Status = StatusCodes.Status422UnprocessableEntity,
                        Extensions = { ["reasonCode"] = WorkspacePolicyReasonCodes.SetupScopeNotAllowed }
                    });
                }

                // The server selects the owner's managed access profile for both journeys (Just me, and With others
                // via the owner access preset); arbitrary role IDs cannot replace initialization.
                request.SecurityRoleIds = null;
            }

            if (request.SecurityRoleIds != null && !WorkspaceMembersController.ValidSecurityRoles(_db, request.SecurityRoleIds))
                return Problem(statusCode: 400, title: "Invalid workspace security roles");

            using (var tx = _db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                if (_workspaces.SlugExists(slug))
                    return Problem(statusCode: 409, title: "Workspace Slug Already Taken");
                var policyScope = _policyTransaction?.BeginExternalScope();
                try
                {
                    var company = _workspaces.Create(
                        UserId, request.Name, slug, storePlan, currency, request.ShortName, options, request.SecurityRoleIds,
                        requestedPlanKey: plan, pendingCheckout: pendingCheckout);
                    var ownerMember = _db.CompanyMembers.Single(m => m.CompanyId == company.Id && m.UserId == UserId);

                    WorkspaceInitializationResult initResult = null;
                    if (wantsNewExperience && _workspaceInit != null)
                    {
                        initResult = _workspaceInit.ApplyNewWorkspace(company.Id, UserId, ownerMember.Id, new WorkspaceInitializationRequest
                        {
                            Purpose = request.WorkspacePurpose.Value,
                            CollaborationIntent = request.CollaborationIntent.Value,
                            OwnerAccessPreset = request.OwnerAccessPreset ?? OwnerAccessPreset.FullWorkspaceAccess,
                            InitialJournalWorkflow = request.InitialJournalWorkflow
                        });

                        if (!initResult.Applied
                            && initResult.ReasonCode is WorkspacePolicyReasonCodes.PersonalPurposeDisabled
                                or WorkspacePolicyReasonCodes.SetupScopeNotAllowed
                                or WorkspacePolicyReasonCodes.EntityPurposeMismatch
                                or WorkspacePolicyReasonCodes.NotPendingSignupWorkspace
                                or WorkspacePolicyReasonCodes.SoloSetupNotEligible
                                or "about_answers_required")
                        {
                            // Early return rolls back the transaction on dispose; discard this scope's work.
                            policyScope?.RolledBack();
                            return UnprocessableEntity(new ProblemDetails
                            {
                                Title = "Workspace initialization rejected",
                                Detail = initResult.Message,
                                Status = StatusCodes.Status422UnprocessableEntity,
                                Extensions = { ["reasonCode"] = initResult.ReasonCode }
                            });
                        }
                    }

                    tx.Commit();
                    // The externally-owned transaction committed; drain ONLY this scope's deferred
                    // committed-state verification for companies touched by the nested policy work. Never throws.
                    tx.Dispose();
                    policyScope?.Committed();

                    string checkoutUrl = null;
                    string checkoutError = null;
                    if (pendingCheckout && _billing != null)
                    {
                        try
                        {
                            var returnUrl = SubscriptionController.ResolveCheckoutReturnUrl(
                                request.ReturnUrl, Request.Headers["Referer"].ToString());
                            var checkout = await _billing.RequestPlanChangeAsync(company.Id, plan, returnUrl, ct);
                            checkoutUrl = checkout.RedirectUrl;
                            checkoutError = checkout.ErrorReason;
                        }
                        catch (ArgumentException ex)
                        {
                            checkoutError = ex.Message;
                        }
                    }

                    return StatusCode(201, Summary(company, CompanyMemberRole.Owner, initResult, checkoutUrl, checkoutError));
                }
                catch (InvalidAccountingPolicyException ex)
                {
                    policyScope?.RolledBack();
                    return Problem(statusCode: 400, title: "Invalid accounting policy", detail: string.Join(" ", ex.Errors));
                }
            }
        }

        private bool TryResolveFreeSelfServicePlan(Dto.Subscription.PlanDefinitionDto requested, out string freePlanKey)
        {
            freePlanKey = null;
            var match = _plans.GetPlans().FirstOrDefault(p =>
                p.SelfService
                && p.MonthlyPrice == 0
                && string.Equals(p.Product, requested.Product, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                return false;
            freePlanKey = match.Key;
            return true;
        }

        public sealed class InitializeCurrentRequest
        {
            [JsonConverter(typeof(StringEnumConverter))] public WorkspacePurpose? WorkspacePurpose { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public WorkspaceCollaborationIntent? CollaborationIntent { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public OwnerAccessPreset? OwnerAccessPreset { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public WorkspaceApprovalRuleKind? InitialJournalWorkflow { get; set; }

            // NOTE: the continuation does NOT accept a plan or accounting-policy fields. Initialization runs
            // on the workspace's existing (free/default) plan and its books provisioned at signup. A plan
            // change — especially to a paid tier — must go through the subscription-change contract
            // (allowed-transition allow-list and provider checkout in the commercial host), which leaves
            // company.Plan untouched until payment is confirmed. Writing the plan here would bypass billing
            // and could not be made atomic with initialization. The owner upgrades afterward via the normal
            // subscription/billing flow.
        }

        /// <summary>
        /// Post-verification continuation for a workspace that was created (slug reserved) during signup but
        /// deliberately left unclassified until the owner verified their email. The authenticated, verified
        /// Owner applies their About answers here to initialize the EXISTING workspace. This does not create
        /// a second workspace and is not the legacy confirmation flow: it runs the same
        /// new-workspace initializer, which only applies to an uninitialized workspace and re-checks
        /// the setup switch and the supported scope against the verified identity.
        /// </summary>
        [TenantScoped]
        [HttpPost("current/initialize")]
        public IActionResult InitializeCurrent([FromBody] InitializeCurrentRequest request)
        {
            if (_workspaceInit == null)
                return Problem(statusCode: 503, title: "Workspace initialization unavailable");

            var companyId = _tenant.RequireCompanyId();
            if (_tenant.Role != CompanyMemberRole.Owner)
                return Problem(statusCode: 403, title: "Only the workspace owner can complete setup.");

            if (!request.WorkspacePurpose.HasValue || !request.CollaborationIntent.HasValue)
                return Problem(statusCode: 400, title: "About answers required",
                    detail: "Both workspacePurpose and collaborationIntent are required for the new workspace experience.");

            // Approved-journey scope at the request boundary (re-enforced in the applicator).
            if (!WorkspaceInitializationScope.IsSupported(request.WorkspacePurpose.Value, request.CollaborationIntent.Value,
                WorkspaceInitializationScope.IsPersonalReleased(Api.Options.WorkspaceExperienceOptions.FromEnvironment(_experience))))
            {
                return UnprocessableEntity(new ProblemDetails
                {
                    Title = "Workspace setup not available",
                    Detail = WorkspaceInitializationScope.UnsupportedScopeMessage,
                    Status = StatusCodes.Status422UnprocessableEntity,
                    Extensions = { ["reasonCode"] = WorkspacePolicyReasonCodes.SetupScopeNotAllowed }
                });
            }

            var ownerMember = _db.CompanyMembers
                .Single(m => m.CompanyId == companyId && m.UserId == UserId && m.Role == CompanyMemberRole.Owner);

            var initResult = _workspaceInit.ApplyNewWorkspace(companyId, UserId, ownerMember.Id, new WorkspaceInitializationRequest
            {
                Purpose = request.WorkspacePurpose.Value,
                CollaborationIntent = request.CollaborationIntent.Value,
                OwnerAccessPreset = request.OwnerAccessPreset ?? OwnerAccessPreset.FullWorkspaceAccess,
                InitialJournalWorkflow = request.InitialJournalWorkflow
            },
            // This continuation initializes a PRE-EXISTING workspace, so require the signup-specific
            // pending-continuation marker: a pristine but non-signup workspace must not be initializable here.
            requireSignupContinuationEvidence: true);

            if (!initResult.Applied)
            {
                // Rejected when the workspace is already initialized OR fails the under-lock pending-signup
                // eligibility proof (legacy, current/former team, transferred owner, invitation or
                // collaboration history). No grants, journal rule, or approval policy are modified.
                return UnprocessableEntity(new ProblemDetails
                {
                    Title = "Workspace initialization rejected",
                    Detail = initResult.Message,
                    Status = StatusCodes.Status422UnprocessableEntity,
                    Extensions = { ["reasonCode"] = initResult.ReasonCode }
                });
            }

            // The workspace is initialized on its existing (free/default) plan. Any upgrade to a paid tier
            // is a separate, billing-gated action through the subscription-change flow; it is intentionally
            // NOT performed here (see InitializeCurrentRequest).
            return Ok(new
            {
                initialized = true,
                plan = _plans.ResolveStoredPlan(_db.Companies.Where(c => c.Id == companyId).Select(c => c.Plan).Single()).Key,
                collaborationState = WorkspaceCollaborationState.SoloActive.ToString(),
                journalWorkflow = initResult.JournalMode.ToString(),
                accessProfileKey = initResult.AccessProfileKey
            });
        }

        [TenantScoped]
        [HttpGet("current")]
        public IActionResult GetCurrent()
        {
            var id = _tenant.RequireCompanyId();
            var company = _db.Companies.Single(c => c.Id == id);
            var fiscalYear = _financial.CurrentFiscalYear();
            var memberId = _db.CompanyMembers
                .Where(m => m.CompanyId == id && m.UserId == UserId)
                .Select(m => (int?)m.Id)
                .FirstOrDefault();
            var operating = _operatingPolicy?.GetSummary(id, memberId, UserId);
            return Ok(new
            {
                id = company.Id, name = company.Name, slug = company.Slug, shortName = company.ShortName,
                plan = _plans.ResolveStoredPlan(company.Plan).Key, activeModules = PlanModuleResolver.ResolveActiveModules(_plans, company), subscriptionStatus = company.SubscriptionStatus,
                role = _tenant.Role.ToString(), permissions = _tenant.Permissions,
                currency = _db.CompanySettings.Select(s => s.Currency).FirstOrDefault() ?? "USD",
                entityType = _db.CompanySettings.Select(s => s.EntityType.ToString()).FirstOrDefault(),
                fiscalYearEnd = fiscalYear?.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                operatingPolicy = operating == null ? null : new
                {
                    purpose = operating.Purpose.ToString(),
                    collaborationIntent = operating.CollaborationIntent.ToString(),
                    collaborationState = operating.CollaborationState.ToString(),
                    policyRevision = operating.PolicyRevision,
                    ownAccessProfileKey = operating.OwnAccessProfileKey,
                    ownAccessProfileDisplayName = operating.OwnAccessProfileDisplayName,
                    journalWorkflow = operating.NormalJournalWorkflow.Mode.ToString(),
                    soloSetupEnabled = operating.SoloSetupEnabled,
                    allowedNextActions = operating.AllowedNextActions,
                    pendingSignupContinuation = operating.PendingSignupContinuation
                }
            });
        }

        [TenantScoped]
        [HttpGet("current/operating-settings")]
        public IActionResult GetOperatingSettings()
        {
            var id = _tenant.RequireCompanyId();
            var memberId = _db.CompanyMembers
                .Where(m => m.CompanyId == id && m.UserId == UserId)
                .Select(m => (int?)m.Id)
                .FirstOrDefault();
            var summary = _operatingPolicy?.GetSummary(id, memberId, UserId);
            if (summary == null)
                return Problem(statusCode: 503, title: "Operating policy unavailable");

            // Reviewer capacity is governance information: only Owners/Admins see it.
            object journalApproval = null;
            if (_tenant.Role is CompanyMemberRole.Owner or CompanyMemberRole.Admin)
            {
                var eligibility = _reviewerEligibility
                    ?? HttpContext?.RequestServices?.GetService<IWorkspaceReviewerEligibility>()
                    ?? new WorkspaceReviewerEligibility(_db, HttpContext?.RequestServices?.GetService<Core.Entitlements.IEntitlementService>());
                var otherReviewers = eligibility.GetEligibleReviewers(id, excludeUserId: UserId);
                journalApproval = new
                {
                    mode = summary.NormalJournalWorkflow.Mode.ToString(),
                    provenance = summary.NormalJournalWorkflow.Provenance?.ToString(),
                    eligibleOtherReviewerCount = otherReviewers.Count,
                    canChange = _tenant.Role == CompanyMemberRole.Owner
                };
            }

            return Ok(new
            {
                journalApproval,
                purpose = summary.Purpose.ToString(),
                collaborationIntent = summary.CollaborationIntent.ToString(),
                collaborationState = summary.CollaborationState.ToString(),
                policyRevision = summary.PolicyRevision,
                ownAccessProfileKey = summary.OwnAccessProfileKey,
                ownAccessProfileDisplayName = summary.OwnAccessProfileDisplayName,
                journalWorkflow = summary.NormalJournalWorkflow.Mode.ToString(),
                soloSetupEnabled = summary.SoloSetupEnabled,
                allowedNextActions = summary.AllowedNextActions,
                pendingSignupContinuation = summary.PendingSignupContinuation
            });
        }

        [TenantScoped]
        [RequireWorkspaceRole(CompanyMemberRole.Owner)]
        [HttpGet("current/solo-setup/eligibility")]
        public IActionResult GetSoloSetupEligibility()
        {
            if (string.IsNullOrWhiteSpace(UserId))
                return Unauthorized();
            var id = _tenant.RequireCompanyId();
            var eligibility = _operatingPolicy?.EvaluateLegacySoloEligibility(id, UserId);
            if (eligibility == null)
                return Problem(statusCode: 503, title: "Operating policy unavailable");
            return Ok(new
            {
                status = eligibility.Status.ToString(),
                reasonCode = eligibility.ReasonCode,
                message = eligibility.Message,
                policyRevision = eligibility.PolicyRevision
            });
        }

        public class SoloSetupConfirmRequest
        {
            public long ExpectedPolicyRevision { get; set; }
        }

        [TenantScoped]
        [RequireWorkspaceRole(CompanyMemberRole.Owner)]
        [HttpPost("current/solo-setup/confirm")]
        public IActionResult ConfirmSoloSetup([FromBody] SoloSetupConfirmRequest request)
        {
            if (string.IsNullOrWhiteSpace(UserId))
                return Unauthorized();
            var id = _tenant.RequireCompanyId();
            var result = _operatingPolicy?.ConfirmLegacySoloSetup(id, UserId, request?.ExpectedPolicyRevision ?? -1);
            if (result == null)
                return Problem(statusCode: 503, title: "Operating policy unavailable");
            if (!result.Applied)
            {
                var status = result.ReasonCode is WorkspacePolicyReasonCodes.StalePolicyRevision
                    or WorkspacePolicyReasonCodes.WorkspacePolicyConflict
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status403Forbidden;
                return StatusCode(status, new ProblemDetails
                {
                    Title = "Solo setup not applied",
                    Detail = result.Message,
                    Status = status,
                    Extensions = { ["reasonCode"] = result.ReasonCode, ["policyRevision"] = result.PolicyRevision }
                });
            }
            return Ok(new
            {
                applied = result.Applied,
                reasonCode = result.ReasonCode,
                message = result.Message,
                policyRevision = result.PolicyRevision,
                collaborationState = result.CollaborationState.ToString()
            });
        }

        public class ReturnToSoloRequest
        {
            public long ExpectedPolicyRevision { get; set; }
            public bool RevokeOutstandingInvitations { get; set; }
        }

        [TenantScoped]
        [RequireWorkspaceRole(CompanyMemberRole.Owner)]
        [HttpPost("current/operating-settings/return-to-solo")]
        public IActionResult ReturnToSolo([FromBody] ReturnToSoloRequest request)
        {
            if (string.IsNullOrWhiteSpace(UserId))
                return Unauthorized();
            var id = _tenant.RequireCompanyId();
            var result = _operatingPolicy?.ReturnToSolo(
                id,
                UserId,
                request?.ExpectedPolicyRevision ?? -1,
                request?.RevokeOutstandingInvitations ?? false);
            if (result == null)
                return Problem(statusCode: 503, title: "Operating policy unavailable");
            if (!result.Applied)
            {
                var status = result.ReasonCode is WorkspacePolicyReasonCodes.StalePolicyRevision
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status422UnprocessableEntity;
                return StatusCode(status, new ProblemDetails
                {
                    Title = "Return to solo not applied",
                    Detail = result.Message,
                    Status = status,
                    Extensions = { ["reasonCode"] = result.ReasonCode, ["policyRevision"] = result.PolicyRevision }
                });
            }
            return Ok(new
            {
                applied = result.Applied,
                reasonCode = result.ReasonCode,
                message = result.Message,
                policyRevision = result.PolicyRevision,
                collaborationState = result.CollaborationState.ToString()
            });
        }

        public class ChangeJournalApprovalPolicyRequest
        {
            public long ExpectedPolicyRevision { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public WorkspaceApprovalRuleKind? JournalWorkflow { get; set; }
        }

        [TenantScoped]
        [RequireWorkspaceRole(CompanyMemberRole.Owner)]
        [HttpPost("current/operating-settings/journal-approval-policy")]
        public IActionResult ChangeJournalApprovalPolicy([FromBody] ChangeJournalApprovalPolicyRequest request)
        {
            if (string.IsNullOrWhiteSpace(UserId))
                return Unauthorized();
            if (request?.JournalWorkflow == null)
                return Problem(statusCode: 400, title: "Journal workflow required",
                    detail: "Choose IndependentReviewerRequired or SingleActorAllowed.");
            var id = _tenant.RequireCompanyId();
            var result = _operatingPolicy?.ChangeJournalApprovalPolicy(
                id,
                UserId,
                request.ExpectedPolicyRevision,
                request.JournalWorkflow.Value);
            if (result == null)
                return Problem(statusCode: 503, title: "Operating policy unavailable");
            if (!result.Applied)
            {
                var status = result.ReasonCode switch
                {
                    WorkspacePolicyReasonCodes.StalePolicyRevision => StatusCodes.Status409Conflict,
                    WorkspacePolicyReasonCodes.OwnerAuthorizationRequired => StatusCodes.Status403Forbidden,
                    WorkspacePolicyReasonCodes.InvalidApprovalRule => StatusCodes.Status400BadRequest,
                    _ => StatusCodes.Status422UnprocessableEntity
                };
                return StatusCode(status, new ProblemDetails
                {
                    Title = "Journal approval policy not changed",
                    Detail = result.Message,
                    Status = status,
                    Extensions = { ["reasonCode"] = result.ReasonCode, ["policyRevision"] = result.PolicyRevision }
                });
            }
            return Ok(new
            {
                applied = result.Applied,
                reasonCode = result.ReasonCode,
                message = result.Message,
                policyRevision = result.PolicyRevision,
                collaborationState = result.CollaborationState.ToString(),
                journalWorkflow = request.JournalWorkflow.Value == WorkspaceApprovalRuleKind.SingleActorAllowed
                    ? WorkspaceApprovalMode.SingleActorAllowed.ToString()
                    : WorkspaceApprovalMode.IndependentReviewerRequired.ToString()
            });
        }

        public class UpdateOrganizationRequest
        {
            public string Name { get; set; }
            public string Slug { get; set; }
            public string Currency { get; set; }
        }

        [TenantScoped]
        [RequireWorkspaceRole(CompanyMemberRole.Admin)]
        [HttpPatch("current")]
        public IActionResult Update([FromBody] UpdateOrganizationRequest request)
        {
            var company = _db.Companies.Single(c => c.Id == _tenant.RequireCompanyId());
            using (var tx = _db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                if (request.Name != null)
                {
                    if (string.IsNullOrWhiteSpace(request.Name))
                        return Problem(statusCode: 400, title: "Company Name Required");
                    company.Name = request.Name.Trim();
                }
                if (request.Slug != null)
                {
                    var slug = WorkspaceService.NormalizeSlug(request.Slug);
                    if (slug.Length == 0 || slug.Length > 100)
                        return Problem(statusCode: 400, title: "Invalid workspace slug");
                    if (!string.Equals(slug, company.Slug, StringComparison.Ordinal) && _workspaces.SlugExists(slug))
                        return Problem(statusCode: 409, title: "Workspace Slug Already Taken");
                    company.Slug = slug;
                }
                if (request.Currency != null)
                {
                    var currency = request.Currency.Trim().ToUpperInvariant();
                    if (!System.Text.RegularExpressions.Regex.IsMatch(currency, "^[A-Z]{3}$"))
                        return Problem(statusCode: 400, title: "Invalid currency");
                    var setting = _db.CompanySettings.FirstOrDefault();
                    if (setting != null && setting.Currency != currency && _accountingPolicy.IsLedgerFrozen(company.Id))
                        return Problem(statusCode: 409, title: "Currency is locked",
                            detail: "The workspace currency cannot change after the first journal entry is posted.");
                    if (setting == null)
                        _db.CompanySettings.Add(new CompanySetting { CompanyId = company.Id, Currency = currency });
                    else
                        setting.Currency = currency;
                }
                _db.SaveChanges();
                tx.Commit();
            }
            return Ok(Summary(company, _tenant.Role.Value));
        }

        public class DeleteOrganizationRequest
        {
            public string ConfirmSlug { get; set; }
        }

        [TenantScoped]
        [RequireWorkspaceRole(CompanyMemberRole.Owner)]
        [Authorize(Policy = "MfaStepUp")]
        [HttpDelete("current")]
        public IActionResult Delete([FromBody] DeleteOrganizationRequest request)
        {
            var company = _db.Companies.Single(c => c.Id == _tenant.RequireCompanyId());
            if (!string.Equals(request.ConfirmSlug, company.Slug, StringComparison.Ordinal))
                return Problem(statusCode: 400, title: "Workspace slug confirmation does not match");
            company.DeletedAt = DateTime.UtcNow;
            _db.SaveChanges();
            return NoContent();
        }

        public class PostalAddressModel
        {
            public string Line1 { get; set; }
            public string Line2 { get; set; }
            public string City { get; set; }
            public string Region { get; set; }
            public string PostalCode { get; set; }
            public string CountryCode { get; set; }
        }

        public class BusinessProfileModel
        {
            public string LegalName { get; set; }
            public string BusinessEmail { get; set; }
            public string BusinessPhone { get; set; }
            public PostalAddressModel Address { get; set; }
        }

        public class SalesInvoiceSettingsModel
        {
            public string SellerName { get; set; } = SalesInvoiceSettings.TradingName;
            public bool ShowSellerAddress { get; set; } = true;
            public bool ShowCustomerEmail { get; set; } = true;
            public bool ShowAttention { get; set; } = true;
            public bool ShowNotes { get; set; } = true;
            public bool ShowPaymentInstructions { get; set; } = true;
            public string DefaultNotes { get; set; }
            public string DefaultPaymentInstructions { get; set; }
            public int? PaymentBankId { get; set; }
        }

        [TenantScoped]
        [HttpGet("current/business-profile")]
        public IActionResult GetBusinessProfile()
        {
            return Ok(ToBusinessProfileModel(_db.CompanySettings.FirstOrDefault()));
        }

        [TenantScoped]
        [RequireWorkspaceRole(CompanyMemberRole.Admin)]
        [HttpPut("current/business-profile")]
        public IActionResult UpdateBusinessProfile([FromBody] BusinessProfileModel request)
        {
            if (request == null)
                return Problem(statusCode: 400, title: "Business profile required");

            var email = Clean(request.BusinessEmail);
            if (email != null && !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
                return Problem(statusCode: 400, title: "Invalid business email");

            var address = new PostalAddress
            {
                Line1 = Clean(request.Address?.Line1),
                Line2 = Clean(request.Address?.Line2),
                City = Clean(request.Address?.City),
                Region = Clean(request.Address?.Region),
                PostalCode = Clean(request.Address?.PostalCode),
                CountryCode = Clean(request.Address?.CountryCode),
            };

            var setting = GetOrCreateSetting();
            setting.LegalName = Clean(request.LegalName);
            setting.BusinessEmail = email;
            setting.BusinessPhone = Clean(request.BusinessPhone);
            setting.BusinessAddress = address.IsEmpty ? null : address;
            _db.SaveChanges();

            return Ok(ToBusinessProfileModel(setting));
        }

        // Read by every member: the invoice editor prefills new invoices from the default texts.
        [TenantScoped]
        [HttpGet("current/sales-invoice-settings")]
        public IActionResult GetSalesInvoiceSettings()
        {
            var settings = _db.CompanySettings.FirstOrDefault()?.GetSalesInvoiceSettings() ?? new SalesInvoiceSettings();
            return Ok(ToSalesInvoiceSettingsModel(settings));
        }

        [TenantScoped]
        [RequireWorkspaceRole(CompanyMemberRole.Admin)]
        [HttpPut("current/sales-invoice-settings")]
        public IActionResult UpdateSalesInvoiceSettings([FromBody] SalesInvoiceSettingsModel request)
        {
            if (request == null)
                return Problem(statusCode: 400, title: "Sales invoice settings required");
            if (!SalesInvoiceSettings.IsValidSellerName(request.SellerName))
                return Problem(statusCode: 400, title: "Invalid seller name",
                    detail: $"Seller name must be '{SalesInvoiceSettings.TradingName}' or '{SalesInvoiceSettings.LegalName}'.");

            var defaultNotes = Clean(request.DefaultNotes);
            var defaultPaymentInstructions = Clean(request.DefaultPaymentInstructions);
            if (defaultNotes?.Length > SalesInvoiceSettings.MaxDefaultTextLength
                || defaultPaymentInstructions?.Length > SalesInvoiceSettings.MaxDefaultTextLength)
                return Problem(statusCode: 400, title: "Default text too long",
                    detail: $"Default notes and payment instructions are limited to {SalesInvoiceSettings.MaxDefaultTextLength} characters.");

            if (request.PaymentBankId.HasValue)
            {
                // Tenant query filter scopes Banks to this workspace, so another workspace's bank reads as missing.
                var paymentBank = _db.Banks.FirstOrDefault(b => b.Id == request.PaymentBankId.Value);
                if (paymentBank == null)
                    return Problem(statusCode: 400, title: "Bank account not found",
                        detail: "Pick one of your bank accounts, or clear the selection.");
                if (paymentBank.ArchivedAt != null)
                    return Problem(statusCode: 400, title: "Bank account is archived",
                        detail: "Archived bank accounts cannot be used for invoice payment details. Pick an active account or clear the selection.");
            }

            var settings = new SalesInvoiceSettings
            {
                SellerName = request.SellerName,
                ShowSellerAddress = request.ShowSellerAddress,
                ShowCustomerEmail = request.ShowCustomerEmail,
                ShowAttention = request.ShowAttention,
                ShowNotes = request.ShowNotes,
                ShowPaymentInstructions = request.ShowPaymentInstructions,
                DefaultNotes = defaultNotes,
                DefaultPaymentInstructions = defaultPaymentInstructions,
                PaymentBankId = request.PaymentBankId,
            };

            var setting = GetOrCreateSetting();
            setting.SetSalesInvoiceSettings(settings);
            _db.SaveChanges();

            return Ok(ToSalesInvoiceSettingsModel(settings));
        }

        public class AccountingPolicyModel
        {
            [JsonConverter(typeof(StringEnumConverter))] public ReportingFramework? ReportingFramework { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public AccountingBasis? AccountingBasis { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public FiscalYearPattern? FiscalYearPattern { get; set; }
            public int? FiscalYearStartMonth { get; set; }
            public DateTime? BooksStartDate { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public TaxRegime? TaxRegime { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public IndustryTemplate? IndustryTemplate { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public EntityType? EntityType { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public InventoryCostingMethod? InventoryCostingMethod { get; set; }
        }

        [TenantScoped]
        [HttpGet("current/accounting-policy")]
        public IActionResult GetAccountingPolicy()
        {
            return Ok(ToPolicyResponse(_db.CompanySettings.FirstOrDefault() ?? new CompanySetting { CompanyId = _tenant.RequireCompanyId() }));
        }

        [TenantScoped]
        [RequireWorkspaceRole(CompanyMemberRole.Admin)]
        [HttpPut("current/accounting-policy")]
        public IActionResult UpdateAccountingPolicy([FromBody] AccountingPolicyModel request)
        {
            if (request == null)
                return Problem(statusCode: 400, title: "Accounting policy required");

            var setting = GetOrCreateSetting();
            var result = _accountingPolicy.Apply(setting, new AccountingPolicyChange
            {
                ReportingFramework = request.ReportingFramework,
                AccountingBasis = request.AccountingBasis,
                FiscalYearPattern = request.FiscalYearPattern,
                FiscalYearStartMonth = request.FiscalYearStartMonth,
                BooksStartDate = request.BooksStartDate,
                TaxRegime = request.TaxRegime,
                IndustryTemplate = request.IndustryTemplate,
                EntityType = request.EntityType,
                InventoryCostingMethod = request.InventoryCostingMethod,
            }, DateTime.UtcNow);

            if (result.ClosedYearsConflict)
                return Problem(statusCode: 409, title: "Cannot change entity type",
                    detail: "Reopen closed years before changing the entity type.");
            if (result.Frozen)
                return Problem(statusCode: 409, title: "Accounting policy is locked",
                    detail: "Reporting framework, basis, fiscal year and books start date cannot change after the first journal entry is posted.");
            if (result.Errors.Count > 0)
                return Problem(statusCode: 400, title: "Invalid accounting policy", detail: string.Join(" ", result.Errors));

            _db.SaveChanges();
            return Ok(ToPolicyResponse(setting));
        }

        [TenantScoped]
        [RequireWorkspaceRole(CompanyMemberRole.Admin)]
        [HttpPost("current/onboarding-checklist/dismiss")]
        public IActionResult DismissOnboardingChecklist()
        {
            var setting = GetOrCreateSetting();
            setting.OnboardingChecklistDismissedAt ??= DateTime.UtcNow;
            _db.SaveChanges();
            return Ok(ToPolicyResponse(setting));
        }

        public class BooksLockModel
        {
            public DateTime? LockedThrough { get; set; }
            public string Reason { get; set; }
        }

        [TenantScoped]
        [RequireModule(SystemModules.Financials)]
        [RequirePermission(SystemPermissions.FinancialsAccountsView)]
        [HttpGet("current/books-lock")]
        public IActionResult GetBooksLock()
        {
            var setting = _db.CompanySettings.FirstOrDefault()
                ?? new CompanySetting { CompanyId = _tenant.RequireCompanyId() };
            var history = _db.BooksLockChanges
                .OrderByDescending(c => c.ChangedAt)
                .ThenByDescending(c => c.Id)
                .Take(20)
                .ToList()
                .Select(c => new
                {
                    previousLockedThrough = FormatDate(c.PreviousLockedThrough),
                    newLockedThrough = FormatDate(c.NewLockedThrough),
                    reason = c.Reason,
                    changedBy = c.ChangedBy,
                    changedAt = c.ChangedAt.ToString("o", CultureInfo.InvariantCulture)
                });
            return Ok(new
            {
                lockedThrough = FormatDate(setting.BooksLockedThrough),
                history
            });
        }

        [TenantScoped]
        [RequireModule(SystemModules.Financials)]
        [RequirePermission(SystemPermissions.FinancialsPeriodsClose)]
        [HttpPut("current/books-lock")]
        public IActionResult UpdateBooksLock([FromBody] BooksLockModel request)
        {
            if (request == null)
                return BadRequest(new { message = "Books lock body is required." });

            var setting = GetOrCreateSetting();
            var previous = setting.BooksLockedThrough?.Date;
            var next = request.LockedThrough?.Date;

            if (next.HasValue)
            {
                var today = DateTime.UtcNow.Date;
                if (next.Value > today)
                    return BadRequest(new { message = "Books lock date cannot be after today." });
                var earliest = setting.BooksStartDate.Date.AddDays(-1);
                if (next.Value < earliest)
                    return BadRequest(new
                    {
                        message = $"Books lock date cannot be before {earliest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} (books start minus one day)."
                    });
            }

            var isReopen = !next.HasValue
                || (previous.HasValue && next.Value < previous.Value);
            if (isReopen && string.IsNullOrWhiteSpace(request.Reason))
                return BadRequest(new { message = "A reason is required when reopening (moving the lock earlier or clearing it)." });

            var latestClosed = _db.FinancialYears
                .Where(fy => fy.CompanyId == setting.CompanyId && fy.ClosedAt != null)
                .OrderByDescending(fy => fy.EndDate)
                .FirstOrDefault();
            if (latestClosed != null)
            {
                var closedThrough = latestClosed.EndDate.Date;
                var movesBeforeClosedYear = !next.HasValue || next.Value < closedThrough;
                if (movesBeforeClosedYear)
                {
                    var code = latestClosed.FiscalYearCode.StartsWith("FY", StringComparison.Ordinal)
                        ? latestClosed.FiscalYearCode
                        : "FY" + latestClosed.FiscalYearCode;
                    return Conflict(new
                    {
                        message = $"{code} is closed. Reopen it under Year-end before moving the lock before {closedThrough.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}."
                    });
                }
            }

            if (previous == next)
                return GetBooksLock();

            setting.BooksLockedThrough = next;
            _db.BooksLockChanges.Add(new BooksLockChange
            {
                CompanyId = setting.CompanyId,
                PreviousLockedThrough = previous,
                NewLockedThrough = next,
                Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
                ChangedBy = GetUserNameFromRequestHeader() ?? UserId ?? "unknown",
                ChangedAt = DateTime.UtcNow
            });
            _db.SaveChanges();

            return GetBooksLock();
        }

        private static string FormatDate(DateTime? date) =>
            date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private object ToPolicyResponse(CompanySetting s) => new
        {
            reportingFramework = s.ReportingFramework.ToString(),
            accountingBasis = s.AccountingBasis.ToString(),
            fiscalYearPattern = s.FiscalYearPattern.ToString(),
            fiscalYearStartMonth = s.FiscalYearStartMonth,
            booksStartDate = s.BooksStartDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            taxRegime = s.TaxRegime.ToString(),
            industryTemplate = s.IndustryTemplate.ToString(),
            entityType = s.EntityType.ToString(),
            inventoryCostingMethod = s.InventoryCostingMethod.ToString(),
            coaTemplateId = s.CoaTemplateId,
            currency = s.Currency,
            isLedgerFrozen = _accountingPolicy.IsLedgerFrozen(s.CompanyId),
            onboardingChecklistDismissedAt = s.OnboardingChecklistDismissedAt,
        };

        private CompanySetting GetOrCreateSetting()
        {
            var setting = _db.CompanySettings.FirstOrDefault();
            if (setting == null)
            {
                setting = new CompanySetting { CompanyId = _tenant.RequireCompanyId() };
                _db.CompanySettings.Add(setting);
            }
            return setting;
        }

        private static string Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static BusinessProfileModel ToBusinessProfileModel(CompanySetting setting)
        {
            var address = setting?.BusinessAddress;
            return new BusinessProfileModel
            {
                LegalName = setting?.LegalName,
                BusinessEmail = setting?.BusinessEmail,
                BusinessPhone = setting?.BusinessPhone,
                Address = new PostalAddressModel
                {
                    Line1 = address?.Line1,
                    Line2 = address?.Line2,
                    City = address?.City,
                    Region = address?.Region,
                    PostalCode = address?.PostalCode,
                    CountryCode = address?.CountryCode,
                },
            };
        }

        private static SalesInvoiceSettingsModel ToSalesInvoiceSettingsModel(SalesInvoiceSettings settings) => new()
        {
            SellerName = settings.SellerName,
            ShowSellerAddress = settings.ShowSellerAddress,
            ShowCustomerEmail = settings.ShowCustomerEmail,
            ShowAttention = settings.ShowAttention,
            ShowNotes = settings.ShowNotes,
            ShowPaymentInstructions = settings.ShowPaymentInstructions,
            DefaultNotes = settings.DefaultNotes,
            DefaultPaymentInstructions = settings.DefaultPaymentInstructions,
            PaymentBankId = settings.PaymentBankId,
        };

        private object Summary(Company company, CompanyMemberRole role, WorkspaceInitializationResult initResult = null,
            string checkoutUrl = null, string checkoutError = null, bool? canAddPersonalBooks = null)
        {
            return new
            {
                isPersonalBooks = _db.PersonalWorkspaces.Any(p => p.CompanyId == company.Id),
                linkedPersonalCompanyId = _db.WorkspaceLinks.Where(l => l.OwnerUserId == UserId && l.WorkCompanyId == company.Id)
                    .Select(l => (int?)l.PersonalCompanyId).FirstOrDefault(),
                linkedWorkCompanyIds = _db.WorkspaceLinks.Where(l => l.OwnerUserId == UserId && l.PersonalCompanyId == company.Id)
                    .Select(l => l.WorkCompanyId).ToList(),
                canAddPersonalBooks = canAddPersonalBooks ?? CanAddPersonalBooks(),
                includedModules = _plans.ResolveStoredPlan(company.Plan).IncludedModules,
                requiredModules = _plans.ResolveStoredPlan(company.Plan).RequiredModules,
                currency = _db.CompanySettings.IgnoreQueryFilters().Where(s => s.CompanyId == company.Id).Select(s => s.Currency).FirstOrDefault(),
                id = company.Id.ToString(), name = company.Name, shortName = company.ShortName, slug = company.Slug,
                plan = _plans.ResolveStoredPlan(company.Plan).Key, subscriptionStatus = company.SubscriptionStatus,
                activationState = string.IsNullOrWhiteSpace(company.ActivationState)
                    ? CompanyActivationStates.Active
                    : company.ActivationState,
                activeModules = PlanModuleResolver.ResolveActiveModules(_plans, company), role = role.ToString(),
                checkoutUrl,
                checkoutError,
                operatingPolicy = initResult == null ? null : new
                {
                    initialized = initResult.Applied,
                    reasonCode = initResult.ReasonCode,
                    message = initResult.Message,
                    policyRevision = initResult.PolicyRevision,
                    accessProfileKey = initResult.AccessProfileKey,
                    journalWorkflow = initResult.JournalMode.ToString()
                }
            };
        }
    }
}
