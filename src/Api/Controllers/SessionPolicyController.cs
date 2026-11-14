// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Linq;
using System.Threading.Tasks;
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
            var workspaceMinutes = SessionTimeoutPolicy.Normalize(
                setting?.IdleTimeoutMinutes ?? SessionTimeoutPolicy.DefaultIdleMinutes);
            return Ok(Describe(workspaceMinutes));
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
            return Ok(Describe(SessionTimeoutPolicy.Normalize(setting.IdleTimeoutMinutes)));
        }

        private object Describe(int workspaceMinutes)
        {
            return new
            {
                idleTimeoutMinutes = workspaceMinutes,
                allowedIdleTimeoutMinutes = SessionTimeoutPolicy.AllowedIdleMinutes,
                absoluteSessionHours = SessionService.AbsoluteHours(_configuration),
                yourSessions = new { idleTimeoutMinutes = workspaceMinutes, setBy = Array.Empty<string>() },
            };
        }
    }
}
