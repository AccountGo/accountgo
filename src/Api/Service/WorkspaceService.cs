#nullable disable
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Api.Data;
using Core.Domain;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Api.Service
{
    public class InvalidAccountingPolicyException : Exception
    {
        public System.Collections.Generic.IReadOnlyList<string> Errors { get; }

        public InvalidAccountingPolicyException(System.Collections.Generic.IReadOnlyList<string> errors)
            : base(string.Join(" ", errors))
        {
            Errors = errors;
        }
    }

    /// <summary>Workspace creation shared by signup and authenticated creation.</summary>
    public class WorkspaceService
    {
        private readonly ApiDbContext _db;
        private readonly CompanyProvisioningService _provisioning;
        private readonly ITenantContext _tenant;
        private readonly Core.Entitlements.IEntitlementService _entitlements;

        public WorkspaceService(ApiDbContext db, CompanyProvisioningService provisioning,
            ITenantContext tenant, Core.Entitlements.IEntitlementService entitlements = null)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _provisioning = provisioning ?? throw new ArgumentNullException(nameof(provisioning));
            _tenant = tenant ?? throw new ArgumentNullException(nameof(tenant));
            _entitlements = entitlements;
        }

        public static string NormalizeSlug(string value)
        {
            var slug = Regex.Replace(value.ToLowerInvariant().Trim(), @"[^a-z0-9\-]", "-");
            return Regex.Replace(slug, @"-+", "-").Trim('-');
        }

        public bool SlugExists(string slug)
        {
            return _provisioning.SlugExists(slug);
        }

        /// <summary>Creates company, ownership and defaults inside the caller's transaction.</summary>
        public Company Create(string userId, string name, string slug, string plan, string currency, string shortName = null,
            CompanyProvisionOptions options = null, System.Collections.Generic.IEnumerable<int> initialSecurityRoleIds = null,
            bool markPendingSignupContinuation = false)
        {
            options = options ?? new CompanyProvisionOptions();
            _entitlements?.EnsureSelfServicePlan(plan);
            _entitlements?.EnsureCanOwnAnotherWorkspace(userId);
            options.Currency = currency;
            var errors = CompanyAccountingPolicyRules.Validate(options, DateTime.UtcNow);
            if (errors.Count > 0)
                throw new InvalidAccountingPolicyException(errors);
            if (options.BooksStartDate == null)
                options.BooksStartDate = new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            if (string.IsNullOrWhiteSpace(options.CoaTemplateId))
                options.CoaTemplateId = CoaTemplates.Resolve(options.IndustryTemplate, options.InventoryCostingMethod);

            var company = new Company
            {
                Name = name.Trim(), Slug = slug,
                ShortName = string.IsNullOrWhiteSpace(shortName) ? name.Trim().Substring(0, Math.Min(4, name.Trim().Length)).ToUpperInvariant() : shortName.Trim(),
                CompanyCode = RandomNumberGenerator.GetInt32(100, 1000).ToString(),
                Plan = plan, SubscriptionStatus = "active", CreatedByUserId = userId,
                AccessBootstrapAvailable = initialSecurityRoleIds == null || !initialSecurityRoleIds.Any()
            };
            _db.Companies.Add(company);
            var member = new CompanyMember
            {
                Company = company, UserId = userId, Role = CompanyMemberRole.Owner,
                Status = CompanyMemberStatus.Active, JoinedAt = DateTime.UtcNow
            };
            if (initialSecurityRoleIds != null)
            {
                foreach (var roleId in initialSecurityRoleIds.Distinct())
                {
                    member.SecurityRoles.Add(new CompanyMemberSecurityRole { SecurityRoleId = roleId });
                }
            }
            _db.CompanyMembers.Add(member);
            _db.SaveChanges();

            // Durable coordination + unclassified operating policy before any feature activation.
            // No automatic solo grant here — explicit initialization owns grants and single-actor rules.
            // Impersonate the new company so ICompanyOwned writes pass tenant enforcement (same as Provision).
            using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
            {
                _db.WorkspacePolicyCoordinations.Add(new WorkspacePolicyCoordination
                {
                    CompanyId = company.Id,
                    CreatedAtUtc = DateTime.UtcNow
                });
                _db.WorkspaceOperatingSettings.Add(new WorkspaceOperatingSettings
                {
                    CompanyId = company.Id,
                    Purpose = WorkspacePurpose.Unspecified,
                    CollaborationIntent = WorkspaceCollaborationIntent.Unspecified,
                    CollaborationState = WorkspaceCollaborationState.LegacyUnclassified,
                    InitializationSource = WorkspaceInitializationSource.Unspecified,
                    PolicyRevision = 0,
                    PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion
                });
                _db.WorkspaceApprovalRules.Add(new WorkspaceApprovalRule
                {
                    CompanyId = company.Id,
                    OperationKey = Core.Authorization.AccessOperationCatalog.FinancialsJournalPost,
                    RuleKind = WorkspaceApprovalRuleKind.IndependentReviewerRequired,
                    Provenance = WorkspaceApprovalRuleProvenance.LegacyEnforced,
                    PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion,
                    ConfiguredAtUtc = DateTime.UtcNow,
                    ConfiguredAtPolicyRevision = 0
                });
                WorkspaceOriginEvidence.Record(_db, company.Id, userId);
                WorkspaceCollaborationEvidence.Record(_db, company.Id, WorkspaceCollaborationEvidence.EventMemberAdded, userId, userId, new { role = CompanyMemberRole.Owner.ToString() });
                if (markPendingSignupContinuation)
                {
                    // Only the signup flow sets this: it creates the workspace unclassified and defers
                    // initialization until the owner verifies their email. This immutable marker — distinct
                    // from generic origin evidence, which every created workspace carries — is what the
                    // authenticated current/initialize continuation requires under the coordination lock.
                    WorkspaceSignupContinuationEvidence.Record(_db, company.Id, userId);
                }
                _db.SaveChanges();
            }

            _provisioning.Provision(company, options);
            return company;
        }
    }
}
