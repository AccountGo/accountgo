#nullable enable
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Core.Domain;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Administration;

namespace Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class OrganizationsController : BaseController
    {
        private readonly ITenantContext _tenantContext;
        private readonly IAdministrationService _adminService;
        private readonly Api.Data.ApiDbContext _db;

        public OrganizationsController(ITenantContext tenantContext, IAdministrationService adminService, Api.Data.ApiDbContext db)
        {
            _tenantContext = tenantContext;
            _adminService = adminService;
            _db = db;
        }

        /// <summary>
        /// Lists only the workspaces the caller is an active member of. Tenant-free: no X-AccountGo-Org needed.
        /// </summary>
        [TenantFree]
        [HttpGet]
        public IActionResult List()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var rows = (from m in _db.CompanyMembers
                        join c in _db.Companies on m.CompanyId equals c.Id
                        where m.UserId == userId && m.Status == Core.Domain.CompanyMemberStatus.Active
                        orderby c.Name
                        select new { Company = c, m.Role })
                .ToList();

            return Ok(rows.Select(r => new
            {
                id = r.Company.Id.ToString(),
                name = r.Company.Name,
                shortName = r.Company.ShortName,
                slug = r.Company.Slug,
                plan = r.Company.Plan ?? "accounting",
                subscriptionStatus = r.Company.SubscriptionStatus ?? "active",
                activeModules = r.Company.GetActiveModules(),
                role = r.Role.ToString()
            }));
        }

        public class CreateOrganizationRequest
        {
            public string Name { get; set; } = string.Empty;
            public string? ShortName { get; set; }
            public string? Slug { get; set; }
            public string? Plan { get; set; } = "accounting";
            public string? Currency { get; set; }
        }

        /// <summary>
        /// Creates a workspace and makes the caller its Owner. Tenant-free: the workspace doesn't exist yet.
        /// </summary>
        [TenantFree]
        [HttpPost]
        public IActionResult Create([FromBody] CreateOrganizationRequest request)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(request?.Name))
                return BadRequest(new ProblemDetails
                {
                    Title = "Company Name Required",
                    Detail = "Company name is required.",
                    Status = StatusCodes.Status400BadRequest
                });

            var rawSlug = !string.IsNullOrWhiteSpace(request.Slug) ? request.Slug : request.Name;
            var cleanSlug = Regex.Replace(rawSlug.ToLowerInvariant().Trim(), @"[^a-z0-9\-]", "-");
            cleanSlug = Regex.Replace(cleanSlug, @"-+", "-").Trim('-');
            if (string.IsNullOrWhiteSpace(cleanSlug))
                cleanSlug = $"org-{RandomNumberGenerator.GetInt32(100, 1000)}";

            if (_adminService.GetCompanyBySlug(cleanSlug) != null)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Workspace Slug Already Taken",
                    Detail = $"A workspace with URL slug '{cleanSlug}' already exists. Please choose a different slug.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            var name = request.Name.Trim();
            var shortName = !string.IsNullOrWhiteSpace(request.ShortName)
                ? request.ShortName.Trim()
                : (name.Length <= 5 ? name.ToUpper() : name.Substring(0, 4).ToUpper());

            var company = new Company
            {
                Name = name,
                ShortName = shortName,
                Slug = cleanSlug,
                CompanyCode = RandomNumberGenerator.GetInt32(100, 1000).ToString(),
                Plan = request.Plan?.ToLowerInvariant() ?? "accounting",
                SubscriptionStatus = "active"
            };

            // Company and the creator's Owner membership are saved together, so a workspace never exists without an owner.
            _db.Companies.Add(company);
            _db.CompanyMembers.Add(new CompanyMember
            {
                Company = company,
                UserId = userId,
                Role = CompanyMemberRole.Owner,
                Status = CompanyMemberStatus.Active,
                JoinedAt = DateTime.UtcNow
            });
            _db.SaveChanges();

            return StatusCode(StatusCodes.Status201Created, new
            {
                id = company.Id.ToString(),
                name = company.Name,
                shortName = company.ShortName,
                slug = company.Slug,
                plan = company.Plan,
                subscriptionStatus = company.SubscriptionStatus,
                activeModules = company.GetActiveModules(),
                role = CompanyMemberRole.Owner.ToString()
            });
        }

        [TenantScoped]
        [HttpGet("current")]
        public IActionResult GetCurrent()
        {
            var companyId = _tenantContext.RequireCompanyId();
            var company = _adminService.GetCompanyBySlug(_tenantContext.CompanySlug ?? "")
                ?? _adminService.GetCompanies().FirstOrDefault(c => c.Id == companyId);

            if (company == null)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Workspace not found",
                    Status = 404,
                    Detail = "Active workspace was not found."
                });
            }

            return Ok(new
            {
                id = company.Id,
                name = company.Name,
                slug = company.Slug ?? $"org-{company.Id}",
                plan = company.Plan ?? "accounting",
                activeModules = company.GetActiveModules(),
                subscriptionStatus = company.SubscriptionStatus ?? "active",
                role = _tenantContext.Role?.ToString(),
                permissions = _tenantContext.Permissions
            });
        }
    }
}
