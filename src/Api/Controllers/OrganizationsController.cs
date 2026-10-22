#nullable disable
using System;
using System.Linq;
using System.Security.Claims;
using Api.Data;
using Api.Filters;
using Api.Service;
using Core.Domain;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/organizations")]
    public class OrganizationsController : BaseController
    {
        private readonly ITenantContext _tenant;
        private readonly ApiDbContext _db;
        private readonly WorkspaceService _workspaces;
        private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;

        public OrganizationsController(ITenantContext tenant, ApiDbContext db, WorkspaceService workspaces)
        {
            _tenant = tenant;
            _db = db;
            _workspaces = workspaces;
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
            return Ok(rows.Select(r => Summary(r.Company, r.Role)));
        }

        public class CreateOrganizationRequest
        {
            public string Name { get; set; } = "";
            public string ShortName { get; set; }
            public string Slug { get; set; }
            public string Plan { get; set; } = "accounting";
            public string Currency { get; set; } = "PHP";
        }

        [TenantFree]
        [HttpPost]
        public IActionResult Create([FromBody] CreateOrganizationRequest request)
        {
            if (string.IsNullOrWhiteSpace(UserId))
                return Unauthorized();
            if (string.IsNullOrWhiteSpace(request.Name))
                return Problem(statusCode: 400, title: "Company Name Required", detail: "Company name is required.");
            var slug = WorkspaceService.NormalizeSlug(string.IsNullOrWhiteSpace(request.Slug) ? request.Name : request.Slug);
            var currency = (request.Currency ?? "PHP").Trim().ToUpperInvariant();
            var plan = (request.Plan ?? "accounting").ToLowerInvariant();
            if (slug.Length == 0 || slug.Length > 100 || !System.Text.RegularExpressions.Regex.IsMatch(currency, "^[A-Z]{3}$") ||
                !new[] { "invoicing", "accounting", "enterprise" }.Contains(plan))
                return Problem(statusCode: 400, title: "Invalid workspace settings");

            using (var tx = _db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                if (_workspaces.SlugExists(slug))
                    return Problem(statusCode: 409, title: "Workspace Slug Already Taken");
                var company = _workspaces.Create(UserId, request.Name, slug, plan, currency, request.ShortName);
                tx.Commit();
                return StatusCode(201, Summary(company, CompanyMemberRole.Owner));
            }
        }

        [TenantScoped]
        [HttpGet("current")]
        public IActionResult GetCurrent()
        {
            var id = _tenant.RequireCompanyId();
            var company = _db.Companies.Single(c => c.Id == id);
            return Ok(new
            {
                id = company.Id, name = company.Name, slug = company.Slug, shortName = company.ShortName,
                plan = company.Plan, activeModules = company.GetActiveModules(), subscriptionStatus = company.SubscriptionStatus,
                role = _tenant.Role.ToString(), permissions = _tenant.Permissions,
                currency = _db.CompanySettings.Select(s => s.Currency).FirstOrDefault() ?? "PHP"
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

        private static object Summary(Company company, CompanyMemberRole role)
        {
            return new
            {
                id = company.Id.ToString(), name = company.Name, shortName = company.ShortName, slug = company.Slug,
                plan = company.Plan, subscriptionStatus = company.SubscriptionStatus,
                activeModules = company.GetActiveModules(), role = role.ToString()
            };
        }
    }
}
