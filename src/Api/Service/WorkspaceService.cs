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
        public Company Create(string userId, string name, string slug, string plan, string currency, string shortName = null)
        {
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
            _provisioning.Provision(company, currency);
            return company;
        }
    }
}
