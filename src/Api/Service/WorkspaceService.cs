#nullable disable
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Api.Data;
using Core.Domain;
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

        public WorkspaceService(ApiDbContext db, CompanyProvisioningService provisioning)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _provisioning = provisioning ?? throw new ArgumentNullException(nameof(provisioning));
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
            CompanyProvisionOptions options = null)
        {
            options = options ?? new CompanyProvisionOptions();
            options.Currency = currency;
            var errors = CompanyAccountingPolicyRules.Validate(options, DateTime.UtcNow);
            if (errors.Count > 0)
                throw new InvalidAccountingPolicyException(errors);
            if (options.BooksStartDate == null)
                options.BooksStartDate = new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            if (string.IsNullOrWhiteSpace(options.CoaTemplateId))
                options.CoaTemplateId = CoaTemplates.Resolve(options.IndustryTemplate, options.ReportingFramework);

            var company = new Company
            {
                Name = name.Trim(), Slug = slug,
                ShortName = string.IsNullOrWhiteSpace(shortName) ? name.Trim().Substring(0, Math.Min(4, name.Trim().Length)).ToUpperInvariant() : shortName.Trim(),
                CompanyCode = RandomNumberGenerator.GetInt32(100, 1000).ToString(),
                Plan = plan, SubscriptionStatus = "active", CreatedByUserId = userId
            };
            _db.Companies.Add(company);
            _db.CompanyMembers.Add(new CompanyMember
            {
                Company = company, UserId = userId, Role = CompanyMemberRole.Owner,
                Status = CompanyMemberStatus.Active, JoinedAt = DateTime.UtcNow
            });
            _db.SaveChanges();
            _provisioning.Provision(company, options);
            return company;
        }
    }
}
