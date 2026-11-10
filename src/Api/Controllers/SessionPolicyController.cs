// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

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
using Microsoft.Extensions.Configuration;

namespace Api.Controllers
{
    public sealed class UpdateSessionPolicyRequest
    {
        public int IdleTimeoutMinutes { get; set; }
    }

    /// <summary>
    /// Workspace automatic sign-out. Changes are recorded by the record-change audit (CompanySetting is tracked)
    /// and apply to each member at their next session refresh.
    /// </summary>
    [Authorize]
    [TenantScoped]
    [ApiController]
    [Route("api/organizations/current/session-policy")]
    public class SessionPolicyController : BaseController
    {
        private readonly ApiDbContext _db;
        private readonly ITenantContext _tenant;
        private readonly IConfiguration _configuration;

        public SessionPolicyController(ApiDbContext db, ITenantContext tenant, IConfiguration configuration)
        {
            _db = db;
            _tenant = tenant;
            _configuration = configuration;
        }

        [HttpGet]
        public IActionResult Get()
        {
            var companyId = _tenant.RequireCompanyId();
            var setting = _db.CompanySettings.AsNoTracking().FirstOrDefault(s => s.CompanyId == companyId);
            return Ok(Describe(setting?.IdleTimeoutMinutes ?? SessionTimeoutPolicy.DefaultIdleMinutes));
        }

        /// <summary>
        /// The caller's own sessions use the strictest timeout across every workspace they belong to. Same rule as
        /// session refresh; lists only the caller's memberships, so it shows which of their workspaces sets it.
        /// </summary>
        private object? DescribeCaller()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (string.IsNullOrEmpty(userId))
                return null;

            var rows = _db.CompanyMembers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(m => m.UserId == userId
                    && m.Status == CompanyMemberStatus.Active
                    && m.Company.DeletedAt == null)
                .Select(m => new
                {
                    m.Company.Name,
                    Minutes = _db.CompanySettings
                        .Where(s => s.CompanyId == m.CompanyId)
                        .Select(s => (int?)s.IdleTimeoutMinutes)
                        .FirstOrDefault() ?? SessionTimeoutPolicy.DefaultIdleMinutes,
                })
                .ToList()
                .Select(r => new { r.Name, Minutes = SessionTimeoutPolicy.Normalize(r.Minutes) })
                .ToList();
            if (rows.Count == 0)
                return null;

            var strictest = rows.Min(r => r.Minutes);
            return new
            {
                idleTimeoutMinutes = strictest,
                setBy = rows.Where(r => r.Minutes == strictest).Select(r => r.Name).OrderBy(n => n).ToList(),
            };
        }

        [RequireWorkspaceRole(CompanyMemberRole.Admin)]
        [HttpPut]
        public IActionResult Update([FromBody] UpdateSessionPolicyRequest request)
        {
            if (request == null || !SessionTimeoutPolicy.IsAllowed(request.IdleTimeoutMinutes))
            {
                return Problem(
                    statusCode: 400,
                    title: "Invalid idle timeout",
                    detail: $"Choose one of: {string.Join(", ", SessionTimeoutPolicy.AllowedIdleMinutes)} minutes.");
            }

            var companyId = _tenant.RequireCompanyId();
            var setting = _db.CompanySettings.FirstOrDefault(s => s.CompanyId == companyId);
            if (setting == null)
            {
                setting = new CompanySetting { CompanyId = companyId };
                _db.CompanySettings.Add(setting);
            }
            setting.IdleTimeoutMinutes = request.IdleTimeoutMinutes;
            _db.SaveChanges();
            return Ok(Describe(setting.IdleTimeoutMinutes));
        }

        private object Describe(int idleTimeoutMinutes) => new
        {
            idleTimeoutMinutes,
            allowedIdleTimeoutMinutes = SessionTimeoutPolicy.AllowedIdleMinutes,
            absoluteSessionHours = SessionService.AbsoluteHours(_configuration),
            yourSessions = DescribeCaller(),
        };
    }
}
